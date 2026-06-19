using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed unsafe class VulkanPreviewRenderer : IDisposable
{
    private readonly Vk _vk;
    private readonly TextOverlayRasterizer _textRasterizer = new();

    private Instance _instance;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;

    private Queue _graphicsQueue;
    private Queue _presentQueue;
    private uint _graphicsQueueFamilyIndex;
    private uint _presentQueueFamilyIndex;

    private KhrSurface? _khrSurface;
    private KhrWin32Surface? _khrWin32Surface;
    private KhrSwapchain? _khrSwapchain;

    private SwapchainKHR _swapchain;
    private Format _swapchainImageFormat;
    private Extent2D _swapchainExtent;
    private Image[] _swapchainImages = Array.Empty<Image>();
    private ImageView[] _swapchainImageViews = Array.Empty<ImageView>();
    private Framebuffer[] _framebuffers = Array.Empty<Framebuffer>();
    private bool[] _swapchainImageInitialized = Array.Empty<bool>();

    private RenderPass _renderPass;
    private PipelineLayout _pipelineLayout;
    private Pipeline _graphicsPipeline;
    private ShaderModule _vertShaderModule;
    private ShaderModule _fragShaderModule;

    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private Sampler _textureSampler;

    private Image _dummyImage;
    private DeviceMemory _dummyMemory;
    private ImageView _dummyImageView;

    private Image _overlayImage;
    private DeviceMemory _overlayMemory;
    private ImageView _overlayImageView;
    private VkBuffer _overlayStagingBuffer;
    private DeviceMemory _overlayStagingMemory;
    private uint _overlayWidth = 1;
    private uint _overlayHeight = 1;
    private bool _overlayHasContent;

    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;

    private Semaphore _imageAvailableSemaphore;
    private Semaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;

    private bool _initialized;
    private bool _disposed;
    private bool _renderResourcesCreated;

    private Image _sourceImage;
    private DeviceMemory _sourceMemory;
    private ImageView _sourceImageView;
    private nint _sourceSharedHandle;
    private uint _sourceWidth;
    private uint _sourceHeight;
    private bool _sourceImageInGeneralLayout;

    private uint _logicalWidth;
    private uint _logicalHeight;
    private int _rotation;

    private string _deviceName = string.Empty;
    private GpuAdapterLuid _deviceLuid;
    private bool _deviceLuidValid;

    public VulkanRendererInfo GetRendererInfo()
    {
        return new VulkanRendererInfo
        {
            DeviceName = _deviceName,
            DeviceLuid = _deviceLuid,
            DeviceLuidValid = _deviceLuidValid,
            SwapchainFormat = _swapchainImageFormat.ToString(),
            SwapchainWidth = _swapchainExtent.Width,
            SwapchainHeight = _swapchainExtent.Height,
            ResolvedShaderRotation = _rotation
        };
    }

    public VulkanPreviewRenderer()
    {
        _vk = Vk.GetApi();
    }

    public string Initialize(IntPtr hwnd, int width, int height)
    {
        ThrowIfDisposed();

        if (_initialized)
            return "Vulkan renderer already initialized.";

        CreateInstance();
        CreateSurface(hwnd);
        PickPhysicalDevice();
        LoadPhysicalDeviceIdentity();
        CreateLogicalDevice();
        CreateSwapchain(width, height);
        CreateCommandPool();
        CreateRenderResources();
        CreateCommandBuffer();
        CreateSyncObjects();
        EnsureOverlayTexture(1, 1, ReadOnlySpan<byte>.Empty);
        UpdateDescriptorSet();

        _initialized = true;

        return GetSelectedGpuDescription();
    }

    public void SetPreviewParams(
        uint logicalWidth,
        uint logicalHeight,
        uint textureWidth,
        uint textureHeight,
        DisplayRotation reportedRotation)
    {
        _logicalWidth = logicalWidth;
        _logicalHeight = logicalHeight;
        _rotation = CapturePreviewGeometry.ResolveShaderRotation(
            reportedRotation,
            new FrameSize(logicalWidth, logicalHeight),
            new FrameSize(textureWidth, textureHeight));
    }

    public void SetPreviewParams(uint logicalWidth, uint logicalHeight, DisplayRotation rotation)
    {
        SetPreviewParams(logicalWidth, logicalHeight, 0, 0, rotation);
    }

    public void SetOverlayText(string text)
    {
        ThrowIfDisposed();

        if (!_initialized)
            return;

        TextOverlayResult overlay = _textRasterizer.Rasterize(text);

        _vk.DeviceWaitIdle(_device);

        EnsureOverlayTexture(overlay.Width, overlay.Height, overlay.Pixels);
        _overlayHasContent = overlay.HasContent;
        UpdateDescriptorSet();
    }

    public void DrawFrame()
    {
        ThrowIfDisposed();

        if (!_initialized || _swapchain.Handle == 0)
            return;

        Fence* fence = stackalloc Fence[] { _inFlightFence };

        _vk.WaitForFences(_device, 1, fence, true, ulong.MaxValue);
        _vk.ResetFences(_device, 1, fence);

        uint imageIndex = 0;

        var acquireResult = _khrSwapchain!.AcquireNextImage(
            _device,
            _swapchain,
            ulong.MaxValue,
            _imageAvailableSemaphore,
            default,
            &imageIndex);

        if (acquireResult == Result.ErrorOutOfDateKhr)
            return;

        if (acquireResult != Result.Success && acquireResult != Result.SuboptimalKhr)
            throw new InvalidOperationException($"AcquireNextImage failed: {acquireResult}");

        RecordRenderCommandBuffer(imageIndex);

        Semaphore* waitSemaphores = stackalloc Semaphore[] { _imageAvailableSemaphore };
        Semaphore* signalSemaphores = stackalloc Semaphore[] { _renderFinishedSemaphore };
        CommandBuffer* commandBuffers = stackalloc CommandBuffer[] { _commandBuffer };

        PipelineStageFlags* waitStages = stackalloc PipelineStageFlags[]
        {
            PipelineStageFlags.ColorAttachmentOutputBit
        };

        void* submitPNext = null;

        DeviceMemory* acquireSyncs = stackalloc DeviceMemory[] { _sourceMemory };
        DeviceMemory* releaseSyncs = stackalloc DeviceMemory[] { _sourceMemory };

        ulong* acquireKeys = stackalloc ulong[] { 1 };
        ulong* releaseKeys = stackalloc ulong[] { 0 };

        uint* acquireTimeouts = stackalloc uint[] { 1_000_000_000 };

        Win32KeyedMutexAcquireReleaseInfoKHR keyedMutexInfo = default;

        if (_sourceMemory.Handle != 0)
        {
            keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
            {
                SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                AcquireCount = 1,
                PAcquireSyncs = acquireSyncs,
                PAcquireKeys = acquireKeys,
                PAcquireTimeouts = acquireTimeouts,
                ReleaseCount = 1,
                PReleaseSyncs = releaseSyncs,
                PReleaseKeys = releaseKeys
            };

            submitPNext = &keyedMutexInfo;
        }

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            PNext = submitPNext,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = waitSemaphores,
            PWaitDstStageMask = waitStages,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = signalSemaphores
        };

        var submitResult = _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence);

        if (submitResult != Result.Success)
            throw new InvalidOperationException($"QueueSubmit failed: {submitResult}");

        SwapchainKHR* swapchains = stackalloc SwapchainKHR[] { _swapchain };
        uint* imageIndices = stackalloc uint[] { imageIndex };

        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = signalSemaphores,
            SwapchainCount = 1,
            PSwapchains = swapchains,
            PImageIndices = imageIndices
        };

        var presentResult = _khrSwapchain.QueuePresent(_presentQueue, &presentInfo);

        if (presentResult != Result.Success && presentResult != Result.SuboptimalKhr)
        {
            if (presentResult == Result.ErrorOutOfDateKhr)
                return;

            throw new InvalidOperationException($"QueuePresent failed: {presentResult}");
        }
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        if (!_initialized || width <= 0 || height <= 0)
            return;

        _vk.DeviceWaitIdle(_device);

        DestroyRenderResources();
        DestroySwapchain();

        CreateSwapchain(width, height);
        CreateRenderResources();

        Array.Clear(_swapchainImageInitialized);
        UpdateDescriptorSet();
    }

    public void SetSourceD3D11SharedTexture(nint sharedHandle, uint width, uint height)
    {
        ThrowIfDisposed();

        if (!_initialized)
            return;

        if (sharedHandle == 0 || width <= 0 || height <= 0)
            return;

        if (_sourceSharedHandle == sharedHandle &&
            _sourceWidth == width &&
            _sourceHeight == height &&
            _sourceImage.Handle != 0)
        {
            return;
        }

        _vk.DeviceWaitIdle(_device);

        DestroySourceImage();

        ImportD3D11TextureAsVulkanImage(sharedHandle, width, height);
        CreateSourceImageView();
        UpdateDescriptorSet();
    }

    public void ClearSource()
    {
        if (!_initialized)
            return;

        _vk.DeviceWaitIdle(_device);
        DestroySourceImage();
        UpdateDescriptorSet();
    }

    private void RecordRenderCommandBuffer(uint imageIndex)
    {
        _vk.ResetCommandBuffer(_commandBuffer, 0);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo
        };

        if (_vk.BeginCommandBuffer(_commandBuffer, &beginInfo) != Result.Success)
            throw new InvalidOperationException("BeginCommandBuffer failed.");

        ImageLayout oldSwapchainLayout = _swapchainImageInitialized[imageIndex]
            ? ImageLayout.PresentSrcKhr
            : ImageLayout.Undefined;

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            oldSwapchainLayout,
            ImageLayout.ColorAttachmentOptimal);

        _swapchainImageInitialized[imageIndex] = true;

        if (_sourceImage.Handle != 0 && !_sourceImageInGeneralLayout)
        {
            TransitionImageLayout(
                _sourceImage,
                ImageLayout.Undefined,
                ImageLayout.General);

            _sourceImageInGeneralLayout = true;
        }

        var clearColor = new ClearValue
        {
            Color = new ClearColorValue
            {
                Float32_0 = 0.06f,
                Float32_1 = 0.08f,
                Float32_2 = 0.13f,
                Float32_3 = 1.00f
            }
        };

        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), _swapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearColor
        };

        _vk.CmdBeginRenderPass(_commandBuffer, &renderPassInfo, SubpassContents.Inline);

        var viewport = new Viewport
        {
            X = 0,
            Y = 0,
            Width = _swapchainExtent.Width,
            Height = _swapchainExtent.Height,
            MinDepth = 0,
            MaxDepth = 1
        };

        var scissor = new Rect2D(new Offset2D(0, 0), _swapchainExtent);

        _vk.CmdSetViewport(_commandBuffer, 0, 1, &viewport);
        _vk.CmdSetScissor(_commandBuffer, 0, 1, &scissor);
        _vk.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Graphics, _graphicsPipeline);

        var descriptorSets = stackalloc DescriptorSet[] { _descriptorSet };
        _vk.CmdBindDescriptorSets(
            _commandBuffer,
            PipelineBindPoint.Graphics,
            _pipelineLayout,
            0,
            1,
            descriptorSets,
            0,
            null);

        uint fitWidth = _logicalWidth > 0 ? _logicalWidth : _sourceWidth;
        uint fitHeight = _logicalHeight > 0 ? _logicalHeight : _sourceHeight;

        if (fitWidth == 0)
            fitWidth = _swapchainExtent.Width;

        if (fitHeight == 0)
            fitHeight = _swapchainExtent.Height;

        var pushConstants = new PreviewPushConstants
        {
            SourceSize = new Vector2(fitWidth, fitHeight),
            ViewportSize = new Vector2(_swapchainExtent.Width, _swapchainExtent.Height),
            Rotation = _rotation,
            HasOverlay = _overlayHasContent ? 1 : 0,
            OverlaySize = new Vector2(_overlayWidth, _overlayHeight)
        };

        _vk.CmdPushConstants(
            _commandBuffer,
            _pipelineLayout,
            ShaderStageFlags.FragmentBit,
            0,
            (uint)Marshal.SizeOf<PreviewPushConstants>(),
            &pushConstants);

        _vk.CmdDraw(_commandBuffer, 3, 1, 0, 0);
        _vk.CmdEndRenderPass(_commandBuffer);

        TransitionImageLayout(
            _swapchainImages[imageIndex],
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.PresentSrcKhr);

        if (_vk.EndCommandBuffer(_commandBuffer) != Result.Success)
            throw new InvalidOperationException("EndCommandBuffer failed.");
    }

    private void CreateRenderResources()
    {
        CompileShaders();
        CreateTextureSampler();
        EnsureDummyImage();
        CreateDescriptorSetLayout();
        CreateDescriptorPoolAndSet();
        CreateRenderPass();
        CreateSwapchainImageViews();
        CreateFramebuffers();
        CreateGraphicsPipeline();
        _renderResourcesCreated = true;
    }

    private void DestroyRenderResources()
    {
        if (!_renderResourcesCreated)
            return;

        if (_graphicsPipeline.Handle != 0)
        {
            _vk.DestroyPipeline(_device, _graphicsPipeline, null);
            _graphicsPipeline = default;
        }

        if (_pipelineLayout.Handle != 0)
        {
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            _pipelineLayout = default;
        }

        if (_renderPass.Handle != 0)
        {
            _vk.DestroyRenderPass(_device, _renderPass, null);
            _renderPass = default;
        }

        DestroyFramebuffers();
        DestroySwapchainImageViews();

        if (_descriptorPool.Handle != 0)
        {
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
            _descriptorPool = default;
            _descriptorSet = default;
        }

        if (_descriptorSetLayout.Handle != 0)
        {
            _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);
            _descriptorSetLayout = default;
        }

        if (_textureSampler.Handle != 0)
        {
            _vk.DestroySampler(_device, _textureSampler, null);
            _textureSampler = default;
        }

        if (_vertShaderModule.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _vertShaderModule, null);
            _vertShaderModule = default;
        }

        if (_fragShaderModule.Handle != 0)
        {
            _vk.DestroyShaderModule(_device, _fragShaderModule, null);
            _fragShaderModule = default;
        }

        _renderResourcesCreated = false;
    }

    private void CompileShaders()
    {
        string vertSource = LoadEmbeddedShader("desktop_preview.vert");
        string fragSource = LoadEmbeddedShader("desktop_preview.frag");

        byte[] vertSpirv = GlslShaderCompiler.Compile(vertSource, ShaderKind.VertexShader, "desktop_preview.vert");
        byte[] fragSpirv = GlslShaderCompiler.Compile(fragSource, ShaderKind.FragmentShader, "desktop_preview.frag");

        _vertShaderModule = CreateShaderModule(vertSpirv);
        _fragShaderModule = CreateShaderModule(fragSpirv);
    }

    private static string LoadEmbeddedShader(string resourceName)
    {
        Assembly assembly = typeof(VulkanPreviewRenderer).Assembly;
        string fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded shader resource not found: {resourceName}");

        using Stream stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Unable to open shader resource: {fullName}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code)
    {
        fixed (byte* codePtr = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)code.Length,
                PCode = (uint*)codePtr
            };

            if (_vk.CreateShaderModule(_device, &createInfo, null, out ShaderModule shaderModule) != Result.Success)
                throw new InvalidOperationException("CreateShaderModule failed.");

            return shaderModule;
        }
    }

    private void CreateTextureSampler()
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
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 0
        };

        if (_vk.CreateSampler(_device, &samplerInfo, null, out _textureSampler) != Result.Success)
            throw new InvalidOperationException("CreateSampler failed.");
    }

    private void EnsureDummyImage()
    {
        if (_dummyImageView.Handle != 0)
            return;

        CreateDeviceImage(
            1,
            1,
            out _dummyImage,
            out _dummyMemory,
            out _dummyImageView,
            new byte[] { 0, 0, 0, 255 });
    }

    private void CreateDescriptorSetLayout()
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[]
        {
            new()
            {
                Binding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            },
            new()
            {
                Binding = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            }
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };

        if (_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, out _descriptorSetLayout) != Result.Success)
            throw new InvalidOperationException("CreateDescriptorSetLayout failed.");
    }

    private void CreateDescriptorPoolAndSet()
    {
        var poolSizes = stackalloc DescriptorPoolSize[]
        {
            new()
            {
                Type = DescriptorType.CombinedImageSampler,
                DescriptorCount = 2
            }
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = poolSizes,
            MaxSets = 1
        };

        if (_vk.CreateDescriptorPool(_device, &poolInfo, null, out _descriptorPool) != Result.Success)
            throw new InvalidOperationException("CreateDescriptorPool failed.");

        var layouts = stackalloc DescriptorSetLayout[] { _descriptorSetLayout };

        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = layouts
        };

        if (_vk.AllocateDescriptorSets(_device, &allocInfo, out _descriptorSet) != Result.Success)
            throw new InvalidOperationException("AllocateDescriptorSets failed.");
    }

    private void UpdateDescriptorSet()
    {
        if (_descriptorSet.Handle == 0)
            return;

        ImageView sourceView = _sourceImageView.Handle != 0 ? _sourceImageView : _dummyImageView;
        ImageLayout sourceLayout = _sourceImageView.Handle != 0 ? ImageLayout.General : ImageLayout.ShaderReadOnlyOptimal;

        var imageInfos = stackalloc DescriptorImageInfo[]
        {
            new()
            {
                Sampler = _textureSampler,
                ImageView = sourceView,
                ImageLayout = sourceLayout
            },
            new()
            {
                Sampler = _textureSampler,
                ImageView = _overlayImageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            }
        };

        var writes = stackalloc WriteDescriptorSet[]
        {
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSet,
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfos[0]
            },
            new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = _descriptorSet,
                DstBinding = 1,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfos[1]
            }
        };

        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
    }

    private void CreateRenderPass()
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = _swapchainImageFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
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
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
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

        if (_vk.CreateRenderPass(_device, &renderPassInfo, null, out _renderPass) != Result.Success)
            throw new InvalidOperationException("CreateRenderPass failed.");
    }

    private void CreateSwapchainImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];

        for (int i = 0; i < _swapchainImages.Length; i++)
        {
            _swapchainImageViews[i] = CreateImageView(_swapchainImages[i], _swapchainImageFormat);
        }
    }

    private void DestroySwapchainImageViews()
    {
        foreach (ImageView imageView in _swapchainImageViews)
        {
            if (imageView.Handle != 0)
                _vk.DestroyImageView(_device, imageView, null);
        }

        _swapchainImageViews = Array.Empty<ImageView>();
    }

    private void CreateFramebuffers()
    {
        _framebuffers = new Framebuffer[_swapchainImageViews.Length];

        for (int i = 0; i < _swapchainImageViews.Length; i++)
        {
            ImageView* attachments = stackalloc ImageView[] { _swapchainImageViews[i] };

            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = attachments,
                Width = _swapchainExtent.Width,
                Height = _swapchainExtent.Height,
                Layers = 1
            };

            if (_vk.CreateFramebuffer(_device, &framebufferInfo, null, out _framebuffers[i]) != Result.Success)
                throw new InvalidOperationException("CreateFramebuffer failed.");
        }
    }

    private void DestroyFramebuffers()
    {
        foreach (Framebuffer framebuffer in _framebuffers)
        {
            if (framebuffer.Handle != 0)
                _vk.DestroyFramebuffer(_device, framebuffer, null);
        }

        _framebuffers = Array.Empty<Framebuffer>();
    }

    private void CreateGraphicsPipeline()
    {
        var shaderStages = stackalloc PipelineShaderStageCreateInfo[]
        {
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            },
            new()
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragShaderModule,
                PName = (byte*)SilkMarshal.StringToPtr("main")
            }
        };

        try
        {
            var pushConstantRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.FragmentBit,
                Offset = 0,
                Size = (uint)Marshal.SizeOf<PreviewPushConstants>()
            };

            DescriptorSetLayout descriptorSetLayout = _descriptorSetLayout;

            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorSetLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };

            if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, out _pipelineLayout) != Result.Success)
                throw new InvalidOperationException("CreatePipelineLayout failed.");

            var dynamicStates = stackalloc DynamicState[]
            {
                DynamicState.Viewport,
                DynamicState.Scissor
            };

            var dynamicStateInfo = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates
            };

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
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

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false,
                RasterizerDiscardEnable = false,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.Clockwise,
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
                BlendEnable = false
            };

            var colorBlend = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
            };

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlend,
                PDynamicState = &dynamicStateInfo,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0
            };

            if (_vk.CreateGraphicsPipelines(
                    _device,
                    default,
                    1,
                    &pipelineInfo,
                    null,
                    out _graphicsPipeline) != Result.Success)
            {
                throw new InvalidOperationException("CreateGraphicsPipelines failed.");
            }
        }
        finally
        {
            SilkMarshal.FreeString((nint)shaderStages[0].PName);
            SilkMarshal.FreeString((nint)shaderStages[1].PName);
        }
    }

    private void EnsureOverlayTexture(uint width, uint height, ReadOnlySpan<byte> rgbaPixels)
    {
        if (_overlayImageView.Handle != 0 &&
            _overlayWidth == width &&
            _overlayHeight == height)
        {
            UploadImagePixels(_overlayImage, width, height, rgbaPixels);
            return;
        }

        DestroyOverlayResources();

        CreateDeviceImage(width, height, out _overlayImage, out _overlayMemory, out _overlayImageView, rgbaPixels);
        _overlayWidth = width;
        _overlayHeight = height;
    }

    private void DestroyOverlayResources()
    {
        if (_overlayStagingBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _overlayStagingBuffer, null);
            _overlayStagingBuffer = default;
        }

        if (_overlayStagingMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _overlayStagingMemory, null);
            _overlayStagingMemory = default;
        }

        if (_overlayImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _overlayImageView, null);
            _overlayImageView = default;
        }

        if (_overlayImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _overlayImage, null);
            _overlayImage = default;
        }

        if (_overlayMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _overlayMemory, null);
            _overlayMemory = default;
        }
    }

    private void CreateDeviceImage(
        uint width,
        uint height,
        out Image image,
        out DeviceMemory memory,
        out ImageView imageView,
        ReadOnlySpan<byte> rgbaPixels)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        if (_vk.CreateImage(_device, &imageInfo, null, out image) != Result.Success)
            throw new InvalidOperationException("CreateImage failed.");

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit)
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out memory) != Result.Success)
            throw new InvalidOperationException("AllocateMemory failed.");

        if (_vk.BindImageMemory(_device, image, memory, 0) != Result.Success)
            throw new InvalidOperationException("BindImageMemory failed.");

        imageView = CreateImageView(image, Format.R8G8B8A8Unorm);
        UploadImagePixels(image, width, height, rgbaPixels);
    }

    private ImageView CreateImageView(Image image, Format format)
    {
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        if (_vk.CreateImageView(_device, &viewInfo, null, out ImageView imageView) != Result.Success)
            throw new InvalidOperationException("CreateImageView failed.");

        return imageView;
    }

    private void UploadImagePixels(Image image, uint width, uint height, ReadOnlySpan<byte> rgbaPixels)
    {
        nuint imageSize = (nuint)(width * height * 4);

        if (rgbaPixels.Length == 0)
        {
            rgbaPixels = new byte[] { 0, 0, 0, 0 };
            imageSize = 4;
        }

        CreateStagingBuffer(imageSize, out VkBuffer stagingBuffer, out DeviceMemory stagingMemory);

        void* mapped = null;

        if (_vk.MapMemory(_device, stagingMemory, 0, imageSize, 0, &mapped) != Result.Success)
            throw new InvalidOperationException("MapMemory failed.");

        fixed (byte* src = rgbaPixels)
        {
            System.Buffer.MemoryCopy(src, mapped, imageSize, Math.Min((nuint)rgbaPixels.Length, imageSize));
        }

        _vk.UnmapMemory(_device, stagingMemory);

        var commandBuffer = BeginOneTimeCommands();

        TransitionImageLayout(commandBuffer, image, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

        var bufferCopy = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };

        _vk.CmdCopyBufferToImage(
            commandBuffer,
            stagingBuffer,
            image,
            ImageLayout.TransferDstOptimal,
            1,
            &bufferCopy);

        TransitionImageLayout(commandBuffer, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        EndOneTimeCommands(commandBuffer);

        _vk.DestroyBuffer(_device, stagingBuffer, null);
        _vk.FreeMemory(_device, stagingMemory, null);
    }

    private void CreateStagingBuffer(nuint size, out VkBuffer buffer, out DeviceMemory memory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };

        if (_vk.CreateBuffer(_device, &bufferInfo, null, out buffer) != Result.Success)
            throw new InvalidOperationException("CreateBuffer failed.");

        _vk.GetBufferMemoryRequirements(_device, buffer, out MemoryRequirements requirements);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out memory) != Result.Success)
            throw new InvalidOperationException("AllocateMemory failed.");

        if (_vk.BindBufferMemory(_device, buffer, memory, 0) != Result.Success)
            throw new InvalidOperationException("BindBufferMemory failed.");
    }

    private CommandBuffer BeginOneTimeCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (_vk.AllocateCommandBuffers(_device, &allocInfo, out CommandBuffer commandBuffer) != Result.Success)
            throw new InvalidOperationException("AllocateCommandBuffers failed.");

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (_vk.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
            throw new InvalidOperationException("BeginCommandBuffer failed.");

        return commandBuffer;
    }

    private void EndOneTimeCommands(CommandBuffer commandBuffer)
    {
        if (_vk.EndCommandBuffer(commandBuffer) != Result.Success)
            throw new InvalidOperationException("EndCommandBuffer failed.");

        var commandBuffers = stackalloc CommandBuffer[] { commandBuffer };

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers
        };

        if (_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default) != Result.Success)
            throw new InvalidOperationException("QueueSubmit failed.");

        _vk.QueueWaitIdle(_graphicsQueue);
        _vk.FreeCommandBuffers(_device, _commandPool, 1, commandBuffers);
    }

    private void ImportD3D11TextureAsVulkanImage(nint sharedHandle, uint width, uint height)
    {
        var externalMemoryImageCreateInfo = new ExternalMemoryImageCreateInfo
        {
            SType = StructureType.ExternalMemoryImageCreateInfo,
            HandleTypes = ExternalMemoryHandleTypeFlags.D3D11TextureBit
        };

        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &externalMemoryImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.B8G8R8A8Unorm,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        if (_vk.CreateImage(_device, &imageCreateInfo, null, out _sourceImage) != Result.Success)
            throw new InvalidOperationException("Create imported Vulkan image failed.");

        _vk.GetImageMemoryRequirements(_device, _sourceImage, out MemoryRequirements memoryRequirements);

        var importMemoryInfo = new ImportMemoryWin32HandleInfoKHR
        {
            SType = StructureType.ImportMemoryWin32HandleInfoKhr,
            HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
            Handle = sharedHandle
        };

        var dedicatedAllocateInfo = new MemoryDedicatedAllocateInfo
        {
            SType = StructureType.MemoryDedicatedAllocateInfo,
            PNext = &importMemoryInfo,
            Image = _sourceImage,
            Buffer = default
        };

        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = &dedicatedAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = FindMemoryType(
                memoryRequirements.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit)
        };

        if (_vk.AllocateMemory(_device, &allocateInfo, null, out _sourceMemory) != Result.Success)
            throw new InvalidOperationException("Allocate imported memory failed.");

        if (_vk.BindImageMemory(_device, _sourceImage, _sourceMemory, 0) != Result.Success)
            throw new InvalidOperationException("Bind imported image memory failed.");

        _sourceSharedHandle = sharedHandle;
        _sourceWidth = width;
        _sourceHeight = height;
        _sourceImageInGeneralLayout = false;
    }

    private void CreateSourceImageView()
    {
        if (_sourceImage.Handle == 0)
            return;

        _sourceImageView = CreateImageView(_sourceImage, Format.B8G8R8A8Unorm);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out PhysicalDeviceMemoryProperties memoryProperties);

        for (uint i = 0; i < memoryProperties.MemoryTypeCount; i++)
        {
            bool typeMatches = (typeFilter & (1u << (int)i)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties;

            if (typeMatches && propertiesMatch)
                return i;
        }

        throw new InvalidOperationException("No suitable Vulkan memory type found.");
    }

    private void DestroySourceImage()
    {
        if (_sourceImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _sourceImageView, null);
            _sourceImageView = default;
        }

        if (_sourceImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _sourceImage, null);
            _sourceImage = default;
        }

        if (_sourceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sourceMemory, null);
            _sourceMemory = default;
        }

        _sourceSharedHandle = 0;
        _sourceWidth = 0;
        _sourceHeight = 0;
        _sourceImageInGeneralLayout = false;
    }

    private void CreateInstance()
    {
        nint appName = SilkMarshal.StringToPtr("WTK MediaForge");
        nint engineName = SilkMarshal.StringToPtr("WTK MediaForge Vulkan");

        nint extSurface = SilkMarshal.StringToPtr(KhrSurface.ExtensionName);
        nint extWin32Surface = SilkMarshal.StringToPtr(KhrWin32Surface.ExtensionName);

        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appName,
                ApplicationVersion = new Version32(0, 1, 0),
                PEngineName = (byte*)engineName,
                EngineVersion = new Version32(0, 1, 0),
                ApiVersion = Vk.Version12
            };

            byte** extensionNames = stackalloc byte*[2];
            extensionNames[0] = (byte*)extSurface;
            extensionNames[1] = (byte*)extWin32Surface;

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
                EnabledExtensionCount = 2,
                PpEnabledExtensionNames = extensionNames
            };

            if (_vk.CreateInstance(&createInfo, null, out _instance) != Result.Success)
                throw new InvalidOperationException("vkCreateInstance failed.");

            if (!_vk.TryGetInstanceExtension<KhrSurface>(_instance, out _khrSurface))
                throw new InvalidOperationException("KHR_surface extension was not loaded.");

            if (!_vk.TryGetInstanceExtension<KhrWin32Surface>(_instance, out _khrWin32Surface))
                throw new InvalidOperationException("KHR_win32_surface extension was not loaded.");
        }
        finally
        {
            SilkMarshal.FreeString(appName);
            SilkMarshal.FreeString(engineName);
            SilkMarshal.FreeString(extSurface);
            SilkMarshal.FreeString(extWin32Surface);
        }
    }

    private void CreateSurface(IntPtr hwnd)
    {
        if (_khrWin32Surface is null)
            throw new InvalidOperationException("KHR_win32_surface is not available.");

        var createInfo = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hinstance = GetModuleHandle(null),
            Hwnd = hwnd
        };

        if (_khrWin32Surface.CreateWin32Surface(_instance, &createInfo, null, out _surface) != Result.Success)
            throw new InvalidOperationException("vkCreateWin32SurfaceKHR failed.");
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan physical devices found.");

        PhysicalDevice* devices = stackalloc PhysicalDevice[(int)deviceCount];
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, devices);

        for (uint i = 0; i < deviceCount; i++)
        {
            var candidate = devices[i];

            if (TryFindQueueFamilies(candidate, out uint graphicsFamily, out uint presentFamily) &&
                SupportsSwapchain(candidate))
            {
                _physicalDevice = candidate;
                _graphicsQueueFamilyIndex = graphicsFamily;
                _presentQueueFamilyIndex = presentFamily;
                return;
            }
        }

        throw new InvalidOperationException("No suitable Vulkan physical device found.");
    }

    private bool TryFindQueueFamilies(
        PhysicalDevice device,
        out uint graphicsFamily,
        out uint presentFamily)
    {
        graphicsFamily = uint.MaxValue;
        presentFamily = uint.MaxValue;

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);

        QueueFamilyProperties* families = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, families);

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            bool supportsGraphics = (families[i].QueueFlags & QueueFlags.GraphicsBit) != 0;
            Bool32 supportsPresent = false;

            _khrSurface!.GetPhysicalDeviceSurfaceSupport(device, i, _surface, &supportsPresent);

            if (supportsGraphics && graphicsFamily == uint.MaxValue)
                graphicsFamily = i;

            if (supportsPresent && presentFamily == uint.MaxValue)
                presentFamily = i;

            if (graphicsFamily != uint.MaxValue && presentFamily != uint.MaxValue)
                return true;
        }

        return false;
    }

    private bool SupportsSwapchain(PhysicalDevice device)
    {
        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, null);

        if (extensionCount == 0)
            return false;

        ExtensionProperties* extensions = stackalloc ExtensionProperties[(int)extensionCount];
        _vk.EnumerateDeviceExtensionProperties(device, (byte*)null, &extensionCount, extensions);

        for (uint i = 0; i < extensionCount; i++)
        {
            string? name = Marshal.PtrToStringAnsi((IntPtr)extensions[i].ExtensionName);

            if (name == KhrSwapchain.ExtensionName)
                return true;
        }

        return false;
    }

    private void CreateLogicalDevice()
    {
        float priority = 1.0f;

        Span<uint> uniqueFamilies = _graphicsQueueFamilyIndex == _presentQueueFamilyIndex
            ? stackalloc uint[] { _graphicsQueueFamilyIndex }
            : stackalloc uint[] { _graphicsQueueFamilyIndex, _presentQueueFamilyIndex };

        DeviceQueueCreateInfo* queueCreateInfos = stackalloc DeviceQueueCreateInfo[uniqueFamilies.Length];

        for (int i = 0; i < uniqueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &priority
            };
        }

        nint swapchainExtensionName = SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName);
        nint externalMemoryExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory");
        nint externalMemoryWin32ExtensionName = SilkMarshal.StringToPtr("VK_KHR_external_memory_win32");
        nint win32KeyedMutexExtensionName = SilkMarshal.StringToPtr("VK_KHR_win32_keyed_mutex");

        try
        {
            byte** enabledExtensions = stackalloc byte*[4];
            enabledExtensions[0] = (byte*)swapchainExtensionName;
            enabledExtensions[1] = (byte*)externalMemoryExtensionName;
            enabledExtensions[2] = (byte*)externalMemoryWin32ExtensionName;
            enabledExtensions[3] = (byte*)win32KeyedMutexExtensionName;

            var features = new PhysicalDeviceFeatures();

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)uniqueFamilies.Length,
                PQueueCreateInfos = queueCreateInfos,
                EnabledExtensionCount = 4,
                PpEnabledExtensionNames = enabledExtensions,
                PEnabledFeatures = &features
            };

            if (_vk.CreateDevice(_physicalDevice, &createInfo, null, out _device) != Result.Success)
                throw new InvalidOperationException("vkCreateDevice failed.");
        }
        finally
        {
            SilkMarshal.FreeString(swapchainExtensionName);
            SilkMarshal.FreeString(externalMemoryExtensionName);
            SilkMarshal.FreeString(externalMemoryWin32ExtensionName);
            SilkMarshal.FreeString(win32KeyedMutexExtensionName);
        }

        _vk.GetDeviceQueue(_device, _graphicsQueueFamilyIndex, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, _presentQueueFamilyIndex, 0, out _presentQueue);

        if (!_vk.TryGetDeviceExtension<KhrSwapchain>(_instance, _device, out _khrSwapchain))
            throw new InvalidOperationException("KHR_swapchain extension was not loaded.");
    }

    private void CreateSwapchain(int width, int height)
    {
        _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(
            _physicalDevice,
            _surface,
            out SurfaceCapabilitiesKHR capabilities);

        SurfaceFormatKHR surfaceFormat = ChooseSurfaceFormat();
        PresentModeKHR presentMode = ChoosePresentMode();

        _swapchainImageFormat = surfaceFormat.Format;
        _swapchainExtent = ChooseSwapExtent(capabilities, width, height);

        uint imageCount = capabilities.MinImageCount + 1;

        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        var imageUsage = ImageUsageFlags.ColorAttachmentBit;

        uint* queueFamilyIndices = stackalloc uint[]
        {
            _graphicsQueueFamilyIndex,
            _presentQueueFamilyIndex
        };

        var sharingMode = _graphicsQueueFamilyIndex == _presentQueueFamilyIndex
            ? SharingMode.Exclusive
            : SharingMode.Concurrent;

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _swapchainImageFormat,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = imageUsage,
            ImageSharingMode = sharingMode,
            QueueFamilyIndexCount = sharingMode == SharingMode.Concurrent ? 2u : 0u,
            PQueueFamilyIndices = sharingMode == SharingMode.Concurrent ? queueFamilyIndices : null,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = default
        };

        if (_khrSwapchain!.CreateSwapchain(_device, &createInfo, null, out _swapchain) != Result.Success)
            throw new InvalidOperationException("CreateSwapchain failed.");

        uint actualImageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &actualImageCount, null);

        Image* images = stackalloc Image[(int)actualImageCount];
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &actualImageCount, images);

        _swapchainImages = new Image[actualImageCount];
        _swapchainImageInitialized = new bool[actualImageCount];

        for (int i = 0; i < actualImageCount; i++)
            _swapchainImages[i] = images[i];
    }

    private SurfaceFormatKHR ChooseSurfaceFormat()
    {
        uint count = 0;
        _khrSurface!.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &count, null);

        if (count == 0)
            throw new InvalidOperationException("No surface formats available.");

        SurfaceFormatKHR* formats = stackalloc SurfaceFormatKHR[(int)count];
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &count, formats);

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Unorm &&
                formats[i].ColorSpace == ColorSpaceKHR.PaceSrgbNonlinearKhr)
            {
                return formats[i];
            }
        }

        for (uint i = 0; i < count; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Srgb &&
                formats[i].ColorSpace == ColorSpaceKHR.PaceSrgbNonlinearKhr)
            {
                return formats[i];
            }
        }

        return formats[0];
    }

    private PresentModeKHR ChoosePresentMode()
    {
        uint count = 0;
        _khrSurface!.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &count, null);

        if (count == 0)
            return PresentModeKHR.FifoKhr;

        PresentModeKHR* modes = stackalloc PresentModeKHR[(int)count];
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &count, modes);

        for (uint i = 0; i < count; i++)
        {
            if (modes[i] == PresentModeKHR.MailboxKhr)
                return PresentModeKHR.MailboxKhr;
        }

        return PresentModeKHR.FifoKhr;
    }

    private static Extent2D ChooseSwapExtent(SurfaceCapabilitiesKHR capabilities, int width, int height)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
            return capabilities.CurrentExtent;

        uint actualWidth = (uint)Math.Max(1, width);
        uint actualHeight = (uint)Math.Max(1, height);

        actualWidth = Math.Clamp(
            actualWidth,
            capabilities.MinImageExtent.Width,
            capabilities.MaxImageExtent.Width);

        actualHeight = Math.Clamp(
            actualHeight,
            capabilities.MinImageExtent.Height,
            capabilities.MaxImageExtent.Height);

        return new Extent2D(actualWidth, actualHeight);
    }

    private void CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        if (_vk.CreateCommandPool(_device, &createInfo, null, out _commandPool) != Result.Success)
            throw new InvalidOperationException("CreateCommandPool failed.");
    }

    private void CreateCommandBuffer()
    {
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (_vk.AllocateCommandBuffers(_device, &allocateInfo, out _commandBuffer) != Result.Success)
            throw new InvalidOperationException("AllocateCommandBuffers failed.");
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };

        if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _imageAvailableSemaphore) != Result.Success)
            throw new InvalidOperationException("Failed to create image available semaphore.");

        if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, out _renderFinishedSemaphore) != Result.Success)
            throw new InvalidOperationException("Failed to create render finished semaphore.");

        if (_vk.CreateFence(_device, &fenceInfo, null, out _inFlightFence) != Result.Success)
            throw new InvalidOperationException("Failed to create in-flight fence.");
    }

    private void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        TransitionImageLayout(_commandBuffer, image, oldLayout, newLayout);
    }

    private void TransitionImageLayout(
        CommandBuffer commandBuffer,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        PipelineStageFlags sourceStage;
        PipelineStageFlags destinationStage;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = 0;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            sourceStage = PipelineStageFlags.TopOfPipeBit;
            destinationStage = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.PresentSrcKhr && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.MemoryReadBit;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            sourceStage = PipelineStageFlags.BottomOfPipeBit;
            destinationStage = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;
            sourceStage = PipelineStageFlags.ColorAttachmentOutputBit;
            destinationStage = PipelineStageFlags.BottomOfPipeBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            sourceStage = PipelineStageFlags.TransferBit;
            destinationStage = PipelineStageFlags.FragmentShaderBit;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported layout transition: {oldLayout} -> {newLayout}");
        }

        _vk.CmdPipelineBarrier(
            commandBuffer,
            sourceStage,
            destinationStage,
            0,
            0,
            null,
            0,
            null,
            1,
            &barrier);
    }

    private void LoadPhysicalDeviceIdentity()
    {
        var idProperties = new PhysicalDeviceIDProperties();

        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &idProperties
        };

        _vk.GetPhysicalDeviceProperties2(_physicalDevice, &properties2);

        _deviceName =
            Marshal.PtrToStringAnsi((IntPtr)properties2.Properties.DeviceName) ??
            "Unknown Vulkan GPU";

        _deviceLuidValid = idProperties.DeviceLuidvalid == true;

        if (_deviceLuidValid)
        {
            _deviceLuid = new GpuAdapterLuid
            {
                LowPart = *(uint*)idProperties.DeviceLuid,
                HighPart = *(int*)(idProperties.DeviceLuid + 4)
            };
        }
        else
        {
            _deviceLuid = GpuAdapterLuid.Empty;
        }
    }

    private string GetSelectedGpuDescription()
    {
        _vk.GetPhysicalDeviceProperties(_physicalDevice, out var properties);

        string deviceName =
            Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ??
            "Unknown Vulkan GPU";

        return $"{deviceName} | Type: {properties.DeviceType} | LUID: {_deviceLuid}";
    }

    private void DestroySwapchain()
    {
        if (_swapchain.Handle != 0 && _khrSwapchain is not null)
        {
            _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }

        _swapchainImages = Array.Empty<Image>();
        _swapchainImageInitialized = Array.Empty<bool>();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_device.Handle != 0)
            _vk.DeviceWaitIdle(_device);

        if (_inFlightFence.Handle != 0)
            _vk.DestroyFence(_device, _inFlightFence, null);

        if (_renderFinishedSemaphore.Handle != 0)
            _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);

        if (_imageAvailableSemaphore.Handle != 0)
            _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);

        if (_commandPool.Handle != 0)
            _vk.DestroyCommandPool(_device, _commandPool, null);

        DestroySourceImage();
        DestroyOverlayResources();
        DestroyRenderResources();

        if (_dummyImageView.Handle != 0)
            _vk.DestroyImageView(_device, _dummyImageView, null);

        if (_dummyImage.Handle != 0)
            _vk.DestroyImage(_device, _dummyImage, null);

        if (_dummyMemory.Handle != 0)
            _vk.FreeMemory(_device, _dummyMemory, null);

        DestroySwapchain();

        if (_device.Handle != 0)
            _vk.DestroyDevice(_device, null);

        if (_surface.Handle != 0 && _khrSurface is not null)
            _khrSurface.DestroySurface(_instance, _surface, null);

        if (_instance.Handle != 0)
            _vk.DestroyInstance(_instance, null);

        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VulkanPreviewRenderer));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
