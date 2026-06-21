using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanCp1ShaderPipelines : IDisposable
{
    private const uint PushConstantMaxSize = 64;

    private readonly VulkanHeadlessDevice _device;
    private readonly Vk _vk;
    private readonly Device _deviceHandle;
    private readonly Sampler _sampler;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly DescriptorPool _descriptorPool;
    private readonly PipelineLayout _pipelineLayout;
    private readonly RenderPass _renderPass;
    private readonly Pipeline _sourceLayerPipeline;
    private readonly Pipeline _outputLetterboxPipeline;
    private readonly ShaderModule _vertexModule;
    private readonly ShaderModule _sourceLayerFragmentModule;
    private readonly ShaderModule _outputLetterboxFragmentModule;
    private bool _disposed;

    public VulkanCp1ShaderPipelines(VulkanHeadlessDevice deviceContext)
    {
        _device = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _vk = deviceContext.Vk;
        _deviceHandle = deviceContext.Device;

        _sampler = CreateSampler();
        _descriptorSetLayout = CreateDescriptorSetLayout();
        _descriptorPool = CreateDescriptorPool();
        _pipelineLayout = CreatePipelineLayout();
        _renderPass = CreateRenderPass();

        _vertexModule = CreateShaderModule(VulkanShaderBytecode.CommonVertex);
        _sourceLayerFragmentModule = CreateShaderModule(VulkanShaderBytecode.SourceLayerFragment);
        _outputLetterboxFragmentModule = CreateShaderModule(VulkanShaderBytecode.OutputLetterboxFragment);

        _sourceLayerPipeline = CreateGraphicsPipeline(_vertexModule, _sourceLayerFragmentModule, enableAlphaBlend: true);
        _outputLetterboxPipeline = CreateGraphicsPipeline(_vertexModule, _outputLetterboxFragmentModule, enableAlphaBlend: false);
    }

    public RenderPass RenderPass => _renderPass;

    public VulkanSubmissionResourceScope CreateSubmissionResourceScope() =>
        new(_vk, _deviceHandle, _descriptorPool);

    public void ComposeOutput(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        RenderCanvasSnapshot canvas,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        var canvasTarget = new VulkanOffscreenRenderTarget(_device, canvas.Size);
        var canvasHandle = new VulkanOffscreenTargetHandle(canvasTarget);
        submissionResources.RetainOffscreenTarget(canvasHandle);
        canvasHandle.Retire();

        RenderCanvasPass(commandBuffer, canvas, output, importsByHandle, canvasTarget, submissionResources);
        RenderOutputPass(commandBuffer, output, canvas.Size, canvasTarget, outputTarget, submissionResources);
    }

    private void RenderCanvasPass(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        RenderOutputStateSnapshot output,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, canvasTarget, canvasTarget.CurrentLayout);
        canvasTarget.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(0, 0, 0, 0)
        };

        var framebuffer = BeginRenderPass(
            commandBuffer,
            canvasTarget,
            canvas.Size,
            clearColor);
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            foreach (var drawObject in canvas.Objects)
            {
                if (drawObject is not RenderSourceLayerDrawObjectSnapshot sourceLayer ||
                    !sourceLayer.Enabled ||
                    sourceLayer.BoundFrame?.Handle is not D3D11SharedTextureFrameHandle sharedHandle)
                {
                    continue;
                }

                if (!importsByHandle.TryGetValue(VulkanExternalTextureKey.From(sharedHandle), out var import))
                    continue;

                var frame = sourceLayer.BoundFrame!.Value;
                TransitionForShaderRead(commandBuffer, import);

                var pushConstants = Cp1PushConstantsBuilder.BuildSourceLayer(sourceLayer, frame, output.CanvasLayoutMode);
                var descriptorSet = AllocateAndWriteDescriptorSet(import.ImageView);
                submissionResources.RetainDescriptorSet(descriptorSet);

                DrawTexturedLayer(
                    commandBuffer,
                    _sourceLayerPipeline,
                    descriptorSet,
                    pushConstants,
                    canvas.Size,
                    sourceLayer.Transform);
            }
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, canvasTarget);
    }

    private void RenderOutputPass(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        FrameSize canvasSize,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, outputTarget, outputTarget.CurrentLayout);
        outputTarget.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var letterbox = output.LetterboxColor;
        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(letterbox.R, letterbox.G, letterbox.B, letterbox.A)
        };

        var framebuffer = BeginRenderPass(
            commandBuffer,
            outputTarget,
            output.OutputSize,
            clearColor);
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var pushConstants = Cp1PushConstantsBuilder.BuildOutputLetterbox(output, canvasSize);
            var descriptorSet = AllocateAndWriteDescriptorSet(canvasTarget.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);

            DrawFullscreen(
                commandBuffer,
                _outputLetterboxPipeline,
                descriptorSet,
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref pushConstants, 1)),
                output.OutputSize);
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, outputTarget);
    }

    private void DrawTexturedLayer(
        CommandBuffer commandBuffer,
        Pipeline pipeline,
        DescriptorSet descriptorSet,
        MediaForgeLayerPushConstants pushConstants,
        FrameSize canvasSize,
        Transform2D transform)
    {
        if (!transform.HasPositiveSize)
            return;

        var viewport = new Viewport
        {
            X = transform.Position.X,
            Y = transform.Position.Y,
            Width = transform.Size.Width,
            Height = transform.Size.Height,
            MinDepth = 0,
            MaxDepth = 1
        };

        var scissor = new Rect2D
        {
            Offset = new Offset2D((int)viewport.X, (int)viewport.Y),
            Extent = new Extent2D((uint)viewport.Width, (uint)viewport.Height)
        };

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &descriptorSet, 0, null);

        var pushBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref pushConstants, 1));
        fixed (byte* pushData = pushBytes)
        {
            _vk.CmdPushConstants(
                commandBuffer,
                _pipelineLayout,
                ShaderStageFlags.FragmentBit,
                0,
                (uint)pushBytes.Length,
                pushData);
        }

        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
        _vk.CmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    private void DrawFullscreen(
        CommandBuffer commandBuffer,
        Pipeline pipeline,
        DescriptorSet descriptorSet,
        ReadOnlySpan<byte> pushConstants,
        FrameSize targetSize)
    {
        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = targetSize.Width,
            Height = targetSize.Height,
            MinDepth = 0,
            MaxDepth = 1
        };

        var scissor = new Rect2D
        {
            Offset = default,
            Extent = new Extent2D(targetSize.Width, targetSize.Height)
        };

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
        _vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &descriptorSet, 0, null);

        fixed (byte* pushData = pushConstants)
        {
            _vk.CmdPushConstants(
                commandBuffer,
                _pipelineLayout,
                ShaderStageFlags.FragmentBit,
                0,
                (uint)pushConstants.Length,
                pushData);
        }

        _vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
        _vk.CmdDraw(commandBuffer, 3, 1, 0, 0);
    }

    private Framebuffer BeginRenderPass(
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget target,
        FrameSize extent,
        ClearValue clearColor)
    {
        var framebuffer = CreateFramebuffer(target.ImageView, extent);

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D
            {
                Offset = default,
                Extent = new Extent2D(extent.Width, extent.Height)
            },
            ClearValueCount = 1,
            PClearValues = &clearColor
        };

        _vk.CmdBeginRenderPass(commandBuffer, &renderPassBegin, SubpassContents.Inline);
        return framebuffer;
    }

    private static void EndRenderPassInstance(Vk vk, CommandBuffer commandBuffer) =>
        vk.CmdEndRenderPass(commandBuffer);

    private void EndRenderPassInstance(CommandBuffer commandBuffer) =>
        EndRenderPassInstance(_vk, commandBuffer);

    private Framebuffer CreateFramebuffer(ImageView imageView, FrameSize extent)
    {
        var attachment = imageView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &attachment,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1
        };

        if (_vk.CreateFramebuffer(_deviceHandle, &framebufferInfo, null, out var framebuffer) != Result.Success)
            throw new InvalidOperationException("vkCreateFramebuffer failed.");

        return framebuffer;
    }

    private DescriptorSet AllocateAndWriteDescriptorSet(ImageView imageView)
    {
        var layout = _descriptorSetLayout;
        var allocateInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        if (_vk.AllocateDescriptorSets(_deviceHandle, &allocateInfo, out var descriptorSet) != Result.Success)
            throw new InvalidOperationException("vkAllocateDescriptorSets failed.");

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler,
            ImageView = imageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_deviceHandle, 1, &write, 0, null);
        return descriptorSet;
    }

    private void TransitionForShaderRead(CommandBuffer commandBuffer, VulkanD3D11TextureImport import)
    {
        var oldLayout = import.CurrentLayout;
        var newLayout = ImageLayout.ShaderReadOnlyOptimal;
        if (oldLayout == newLayout)
            return;

        VulkanImageLayoutTransition.Transition(_vk, commandBuffer, import.Image, oldLayout, newLayout);
        import.SetLayout(newLayout);
    }

    private static void TransitionForColorAttachment(
        Vk vk,
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget target,
        ImageLayout currentLayout)
    {
        if (currentLayout == ImageLayout.ColorAttachmentOptimal)
            return;

        VulkanImageLayoutTransition.Transition(
            vk,
            commandBuffer,
            target.Image,
            currentLayout,
            ImageLayout.ColorAttachmentOptimal);
    }

    private static void TransitionToShaderRead(
        Vk vk,
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget target)
    {
        VulkanImageLayoutTransition.Transition(
            vk,
            commandBuffer,
            target.Image,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.ShaderReadOnlyOptimal);
        target.CurrentLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_sourceLayerPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _sourceLayerPipeline, null);

        if (_outputLetterboxPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _outputLetterboxPipeline, null);

        if (_pipelineLayout.Handle != 0)
            _vk.DestroyPipelineLayout(_deviceHandle, _pipelineLayout, null);

        if (_renderPass.Handle != 0)
            _vk.DestroyRenderPass(_deviceHandle, _renderPass, null);

        if (_descriptorPool.Handle != 0)
            _vk.DestroyDescriptorPool(_deviceHandle, _descriptorPool, null);

        if (_descriptorSetLayout.Handle != 0)
            _vk.DestroyDescriptorSetLayout(_deviceHandle, _descriptorSetLayout, null);

        if (_sampler.Handle != 0)
            _vk.DestroySampler(_deviceHandle, _sampler, null);

        DestroyShaderModule(_sourceLayerFragmentModule);
        DestroyShaderModule(_outputLetterboxFragmentModule);
        DestroyShaderModule(_vertexModule);
    }

    private void DestroyShaderModule(ShaderModule module)
    {
        if (module.Handle != 0)
            _vk.DestroyShaderModule(_deviceHandle, module, null);
    }

    private Sampler CreateSampler()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            AnisotropyEnable = false,
            MaxAnisotropy = 1,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 0
        };

        if (_vk.CreateSampler(_deviceHandle, &samplerInfo, null, out var sampler) != Result.Success)
            throw new InvalidOperationException("vkCreateSampler failed.");

        return sampler;
    }

    private DescriptorSetLayout CreateDescriptorSetLayout()
    {
        var samplerBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerBinding
        };

        if (_vk.CreateDescriptorSetLayout(_deviceHandle, &layoutInfo, null, out var layout) != Result.Success)
            throw new InvalidOperationException("vkCreateDescriptorSetLayout failed.");

        return layout;
    }

    private DescriptorPool CreateDescriptorPool()
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 16
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = 16,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };

        if (_vk.CreateDescriptorPool(_deviceHandle, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("vkCreateDescriptorPool failed.");

        return pool;
    }

    private PipelineLayout CreatePipelineLayout()
    {
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = PushConstantMaxSize
        };

        var setLayout = _descriptorSetLayout;
        var layoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };

        if (_vk.CreatePipelineLayout(_deviceHandle, &layoutInfo, null, out var layout) != Result.Success)
            throw new InvalidOperationException("vkCreatePipelineLayout failed.");

        return layout;
    }

    private RenderPass CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = Format.R8G8B8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal
        };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        if (_vk.CreateRenderPass(_deviceHandle, &renderPassInfo, null, out var renderPass) != Result.Success)
            throw new InvalidOperationException("vkCreateRenderPass failed.");

        return renderPass;
    }

    private Pipeline CreateGraphicsPipeline(
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        bool enableAlphaBlend)
    {
        var entryPoint = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexModule,
                PName = entryPoint
            };

            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentModule,
                PName = entryPoint
            };
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = false
            };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1
            };

            var rasterization = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = false
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                SampleShadingEnable = false,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit |
                                 ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit |
                                 ColorComponentFlags.ABit,
                BlendEnable = enableAlphaBlend,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add
            };

            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var dynamicStates = stackalloc DynamicState[]
            {
                DynamicState.Viewport,
                DynamicState.Scissor
            };

            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicState,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0
            };

            if (_vk.CreateGraphicsPipelines(_deviceHandle, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                throw new InvalidOperationException("vkCreateGraphicsPipelines failed.");

            return pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)entryPoint);
        }
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> spirv)
    {
        fixed (byte* code = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code
            };

            if (_vk.CreateShaderModule(_deviceHandle, &createInfo, null, out var module) != Result.Success)
                throw new InvalidOperationException("vkCreateShaderModule failed.");

            return module;
        }
    }
}
