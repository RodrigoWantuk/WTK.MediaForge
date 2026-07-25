using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Text;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanCompositionShaderPipelines : IDisposable
{
    private const uint PushConstantMaxSize = 128;
    private const uint MaxDescriptorSetsPerSubmit = 256;
    private const int MaxNestedCanvasDepth = 8;

    private readonly VulkanHeadlessDevice _device;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Vk _vk;
    private readonly Device _deviceHandle;
    private readonly Sampler _sampler;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly DescriptorPool _descriptorPool;
    private readonly PipelineLayout _pipelineLayout;
    private readonly RenderPass _renderPass;
    private readonly RenderPass _loadRenderPass;
    private readonly Pipeline _sourceLayerPipeline;
    private readonly Pipeline _solidPipeline;
    private readonly Pipeline _canvasCompositePipeline;
    private readonly Pipeline _outputLetterboxPipeline;
    private readonly Pipeline _outputLetterboxBlendPipeline;
    private readonly Pipeline _textPipeline;
    private readonly Pipeline _blurPipeline;
    private readonly Pipeline _maskCompositePipeline;
    private readonly ShaderModule _vertexModule;
    private readonly ShaderModule _sourceLayerFragmentModule;
    private readonly ShaderModule _solidFragmentModule;
    private readonly ShaderModule _canvasCompositeFragmentModule;
    private readonly ShaderModule _outputLetterboxFragmentModule;
    private readonly ShaderModule _textFragmentModule;
    private readonly ShaderModule _blurFragmentModule;
    private readonly ShaderModule _maskCompositeFragmentModule;
    private readonly VulkanIntermediateTargetPool _intermediateTargetPool;
    private readonly VulkanSubmissionResourceMetrics _submissionMetrics = new();
    private VulkanFontAtlasBridge? _fontAtlasBridge;
    private bool _disposed;

    public VulkanCompositionShaderPipelines(
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        VulkanGpuResourcePool? gpuResourcePool = null)
    {
        _device = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
        _intermediateTargetPool = new VulkanIntermediateTargetPool(
            gpuResourcePool ?? new VulkanGpuResourcePool(_device));
        _vk = deviceContext.Vk;
        _deviceHandle = deviceContext.Device;

        _sampler = CreateSampler();
        _descriptorSetLayout = CreateDescriptorSetLayout();
        _descriptorPool = CreateDescriptorPool();
        _pipelineLayout = CreatePipelineLayout();
        _renderPass = CreateRenderPass(AttachmentLoadOp.Clear);
        _loadRenderPass = CreateRenderPass(AttachmentLoadOp.Load);

        _vertexModule = CreateShaderModule(VulkanShaderBytecode.CommonVertex);
        _sourceLayerFragmentModule = CreateShaderModule(VulkanShaderBytecode.SourceLayerFragment);
        _solidFragmentModule = CreateShaderModule(VulkanShaderBytecode.SolidFragment);
        _canvasCompositeFragmentModule = CreateShaderModule(VulkanShaderBytecode.CanvasCompositeFragment);
        _outputLetterboxFragmentModule = CreateShaderModule(VulkanShaderBytecode.OutputLetterboxFragment);
        _textFragmentModule = CreateShaderModule(VulkanShaderBytecode.TextFragment);
        _blurFragmentModule = CreateShaderModule(VulkanShaderBytecode.BlurFragment);
        _maskCompositeFragmentModule = CreateShaderModule(VulkanShaderBytecode.MaskCompositeFragment);

        _sourceLayerPipeline = CreateGraphicsPipeline(_vertexModule, _sourceLayerFragmentModule, enableAlphaBlend: true);
        _solidPipeline = CreateGraphicsPipeline(_vertexModule, _solidFragmentModule, enableAlphaBlend: true);
        _canvasCompositePipeline = CreateGraphicsPipeline(_vertexModule, _canvasCompositeFragmentModule, enableAlphaBlend: true);
        _outputLetterboxPipeline = CreateGraphicsPipeline(_vertexModule, _outputLetterboxFragmentModule, enableAlphaBlend: false);
        _outputLetterboxBlendPipeline = CreateGraphicsPipeline(_vertexModule, _outputLetterboxFragmentModule, enableAlphaBlend: true);
        _textPipeline = CreateGraphicsPipeline(_vertexModule, _textFragmentModule, enableAlphaBlend: true);
        _blurPipeline = CreateGraphicsPipeline(_vertexModule, _blurFragmentModule, enableAlphaBlend: false);
        _maskCompositePipeline = CreateGraphicsPipeline(_vertexModule, _maskCompositeFragmentModule, enableAlphaBlend: false);
    }

    internal void SetFontAtlasBridge(VulkanFontAtlasBridge fontAtlasBridge) =>
        _fontAtlasBridge = fontAtlasBridge ?? throw new ArgumentNullException(nameof(fontAtlasBridge));

    public RenderPass RenderPass => _renderPass;

    internal int IntermediateTargetPoolLiveCountForTests =>
        _intermediateTargetPool.LiveEntryCountForTests;

    internal VulkanIntermediateTargetPoolMetrics IntermediateTargetMetrics =>
        _intermediateTargetPool.GetMetricsSnapshot();

    internal VulkanSubmissionResourceMetricsSnapshot SubmissionResourceMetrics =>
        _submissionMetrics.GetSnapshot();

    public void InvalidateIntermediateTargets() => _intermediateTargetPool.InvalidateAll();

    public VulkanSubmissionResourceScope CreateSubmissionResourceScope() =>
        new(_vk, _deviceHandle, _descriptorPool, _submissionMetrics);

    public void ComposeOutput(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        RenderCanvasSnapshot canvas,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        var canvasTarget = RenderCanvasToIntermediateTarget(
            commandBuffer,
            canvas,
            output,
            importsByHandle,
            submissionResources);

        RenderOutputPass(commandBuffer, output, canvas.Size, canvasTarget, outputTarget, submissionResources);
    }

    internal VulkanOffscreenRenderTarget RenderCanvasToIntermediateTarget(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        RenderOutputStateSnapshot output,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyDictionary<DrawObjectId, VulkanOffscreenRenderTarget>? physicalBlurTargets = null)
    {
        var canvasHandle = _intermediateTargetPool.Rent(canvas.PhysicalKey, canvas.Size);
        submissionResources.RetainOffscreenTarget(canvasHandle);
        var canvasTarget = (VulkanOffscreenRenderTarget)canvasHandle.Target;
        canvasHandle.Retire();

        var composedCanvasTarget = RenderCanvasPass(
            commandBuffer,
            canvas,
            output,
            importsByHandle,
            canvasTarget,
            submissionResources,
            depth: 0,
            physicalBlurTargets);
        return RenderCanvasEffects(
            commandBuffer,
            canvas,
            composedCanvasTarget,
            submissionResources);
    }

    private VulkanOffscreenRenderTarget RenderCanvasEffects(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Canvas, canvas.Effects);
        if (plan.IsEmpty)
            return canvasTarget;

        var current = canvasTarget;
        byte salt = 32;
        foreach (var effect in plan.OrderedEffects)
        {
            var input = current;

            switch (effect)
            {
                case ColorCorrectionEffectSnapshot colorCorrection:
                {
                    var output = RentCanvasEffectTarget(canvas, salt++, submissionResources);
                    RenderCanvasColorCorrectionPass(
                        commandBuffer,
                        current,
                        output,
                        colorCorrection,
                        submissionResources);
                    current = output;
                    break;
                }

                case BlurEffectSnapshot blur:
                {
                    var horizontal = RentCanvasEffectTarget(canvas, salt++, submissionResources);
                    var output = RentCanvasEffectTarget(canvas, salt++, submissionResources);
                    RenderBlurPass(commandBuffer, current, horizontal, blur.Radius, horizontal: true, submissionResources);
                    RenderBlurPass(commandBuffer, horizontal, output, blur.Radius, horizontal: false, submissionResources);
                    current = output;
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Effect '{effect.Name}' is not supported in the canvas execution path.");
            }

            if (effect.Mask is not { Enabled: true } mask)
                continue;

            EnsureSupportedGeometricMask($"Canvas effect '{effect.Name}'", mask);
            var composited = RentCanvasEffectTarget(canvas, salt++, submissionResources);
            RenderMaskCompositePass(commandBuffer, input, current, composited, mask, submissionResources);
            current = composited;
        }

        return current;
    }

    private VulkanOffscreenRenderTarget RentCanvasEffectTarget(
        RenderCanvasSnapshot canvas,
        byte salt,
        VulkanSubmissionResourceScope submissionResources)
    {
        var handle = _intermediateTargetPool.Rent(
            canvas.PhysicalKey.Derive($"canvas-effect:{salt}"),
            canvas.Size);
        submissionResources.RetainOffscreenTarget(handle);
        var target = (VulkanOffscreenRenderTarget)handle.Target;
        handle.Retire();
        return target;
    }

    private VulkanOffscreenRenderTarget RenderAdjustmentEffects(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        RenderAdjustmentLayerDrawObjectSnapshot adjustment,
        VulkanOffscreenRenderTarget input,
        VulkanSubmissionResourceScope submissionResources)
    {
        if (adjustment.TargetMode != AdjustmentLayerTargetMode.LayersBelow)
        {
            throw new MediaForgeUnsupportedFeatureException(
                "render.adjustment_layer.target_mode",
                $"Adjustment layer '{adjustment.Name}' has unsupported target mode '{adjustment.TargetMode}'.");
        }

        var plan = EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, adjustment.Effects);
        var current = input;
        byte salt = 1;
        foreach (var effect in plan.OrderedEffects)
        {
            if (effect.Mask is not null)
            {
                throw new MediaForgeUnsupportedFeatureException(
                    "render.effect.mask",
                    $"Effect '{effect.Name}' on adjustment layer '{adjustment.Name}' requires the Vulkan mask-composite pipeline.");
            }

            switch (effect)
            {
                case ColorCorrectionEffectSnapshot colorCorrection:
                {
                    var output = RentAdjustmentEffectTarget(canvas, adjustment.Id, salt++, submissionResources);
                    RenderCanvasColorCorrectionPass(commandBuffer, current, output, colorCorrection, submissionResources);
                    current = output;
                    break;
                }

                case BlurEffectSnapshot blur:
                {
                    var horizontal = RentAdjustmentEffectTarget(canvas, adjustment.Id, salt++, submissionResources);
                    var output = RentAdjustmentEffectTarget(canvas, adjustment.Id, salt++, submissionResources);
                    RenderBlurPass(commandBuffer, current, horizontal, blur.Radius, horizontal: true, submissionResources);
                    RenderBlurPass(commandBuffer, horizontal, output, blur.Radius, horizontal: false, submissionResources);
                    current = output;
                    break;
                }

                default:
                    throw new MediaForgeUnsupportedFeatureException(
                        "render.adjustment_layer.effect",
                        $"Effect '{effect.Name}' is not supported by the Vulkan adjustment-layer execution path.");
            }
        }

        if (adjustment.Mask is not { Enabled: true } mask)
            return current;

        EnsureSupportedGeometricMask($"Adjustment layer '{adjustment.Name}'", mask);
        var composited = RentAdjustmentEffectTarget(canvas, adjustment.Id, 250, submissionResources);
        RenderMaskCompositePass(commandBuffer, input, current, composited, mask, submissionResources);
        return composited;
    }

    private static void EnsureSupportedGeometricMask(string owner, EffectMaskStateSnapshot mask)
    {
        if (!mask.Transform.Equals(Transform2D.Default))
        {
            throw new MediaForgeUnsupportedFeatureException(
                "render.effect.mask.transform",
                $"{owner} uses a mask transform, which is not yet supported by the Vulkan mask-composite pipeline.");
        }

        if (mask is RectangleEffectMaskStateSnapshot or RoundedRectangleEffectMaskStateSnapshot or EllipseEffectMaskStateSnapshot)
            return;

        throw new MediaForgeUnsupportedFeatureException(
            "render.effect.mask",
            $"{owner} uses mask '{mask.GetType().Name}', which requires GPU mask-asset support.");
    }

    private VulkanOffscreenRenderTarget RentAdjustmentEffectTarget(
        RenderCanvasSnapshot canvas,
        DrawObjectId adjustmentId,
        byte salt,
        VulkanSubmissionResourceScope submissionResources)
    {
        var handle = _intermediateTargetPool.Rent(
            canvas.PhysicalKey.Derive($"adjustment-effect:{adjustmentId.Value:N}:{salt}"),
            canvas.Size);
        submissionResources.RetainOffscreenTarget(handle);
        var target = (VulkanOffscreenRenderTarget)handle.Target;
        handle.Retire();
        return target;
    }

    private void RenderCanvasColorCorrectionPass(
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget input,
        VulkanOffscreenRenderTarget output,
        ColorCorrectionEffectSnapshot colorCorrection,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, output, output.CurrentLayout);
        output.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var framebuffer = BeginRenderPass(
            commandBuffer,
            output,
            output.Size,
            new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) });
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var layer = CreateCanvasEffectLayer(input.Size);
            var frame = new GpuFrameReference
            {
                LogicalSize = input.Size,
                TextureSize = input.Size,
                PixelFormat = "RGBA8"
            };
            var pushConstants = CompositionPushConstantsBuilder.BuildSourceLayer(
                layer,
                frame,
                chromaKey: null,
                colorCorrection);
            var descriptorSet = AllocateAndWriteDescriptorSet(input.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);
            DrawTexturedLayer(
                commandBuffer,
                _sourceLayerPipeline,
                descriptorSet,
                pushConstants,
                output.Size,
                layer.Transform);
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, output);
    }

    private void RenderMaskCompositePass(
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget original,
        VulkanOffscreenRenderTarget effectResult,
        VulkanOffscreenRenderTarget output,
        EffectMaskStateSnapshot mask,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, output, output.CurrentLayout);
        output.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var framebuffer = BeginRenderPass(
            commandBuffer,
            output,
            output.Size,
            new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) });
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var pushConstants = CompositionPushConstantsBuilder.BuildMaskComposite(mask);
            var descriptorSet = AllocateAndWriteDescriptorSet(original.ImageView, effectResult.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);
            DrawFullscreen(
                commandBuffer,
                _maskCompositePipeline,
                descriptorSet,
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref pushConstants, 1)),
                output.Size);
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, output);
    }

    private static RenderSourceLayerDrawObjectSnapshot CreateCanvasEffectLayer(FrameSize size) =>
        new()
        {
            Name = "Canvas effect input",
            Transform = new Transform2D { Size = new CanvasSize(size.Width, size.Height) },
            EffectiveCrop = NormalizedRect.Full,
            Opacity = 1f,
            LayoutMode = LayoutMode.Stretch,
            LetterboxColor = Core.Color.ColorRgba.Transparent
        };

    internal IReadOnlyDictionary<DrawObjectId, VulkanOffscreenRenderTarget> RenderBlurEffectIntermediateTargets(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyCollection<DrawObjectId> drawObjectIds)
    {
        if (drawObjectIds.Count == 0)
            return new Dictionary<DrawObjectId, VulkanOffscreenRenderTarget>();

        var effectResolutions = ResolveEffectsForCanvas(canvas);
        return RenderBlurredSourceTargets(
            commandBuffer,
            canvas,
            importsByHandle,
            submissionResources,
            effectResolutions,
            drawObjectIds);
    }

    internal void ComposeOutputFromCanvasTarget(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        FrameSize canvasSize,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        RenderOutputPass(commandBuffer, output, canvasSize, canvasTarget, outputTarget, submissionResources);
    }

    internal void ComposeOutputOverlayFromCanvasTarget(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        FrameSize canvasSize,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanOffscreenRenderTarget outputTarget,
        float opacity,
        VulkanSubmissionResourceScope submissionResources)
    {
        RenderOutputOverlayPass(commandBuffer, output, canvasSize, canvasTarget, outputTarget, opacity, submissionResources);
    }

    public void ComposeTransitionOutput(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        RenderCanvasSnapshot previousCanvas,
        RenderCanvasSnapshot currentCanvas,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources)
    {
        var progress = Math.Clamp(output.RouteTransitionProgress, 0f, 1f);
        if (progress >= 1f)
        {
            ComposeOutput(
                commandBuffer,
                output,
                currentCanvas,
                importsByHandle,
                outputTarget,
                submissionResources);
            return;
        }

        if (progress <= 0f)
        {
            ComposeOutput(
                commandBuffer,
                output,
                previousCanvas,
                importsByHandle,
                outputTarget,
                submissionResources);
            return;
        }

        var previousHandle = _intermediateTargetPool.Rent(previousCanvas.PhysicalKey, previousCanvas.Size);
        submissionResources.RetainOffscreenTarget(previousHandle);
        var previousTarget = (VulkanOffscreenRenderTarget)previousHandle.Target;
        previousHandle.Retire();

        var currentHandle = _intermediateTargetPool.Rent(currentCanvas.PhysicalKey, currentCanvas.Size);
        submissionResources.RetainOffscreenTarget(currentHandle);
        var currentTarget = (VulkanOffscreenRenderTarget)currentHandle.Target;
        currentHandle.Retire();

        previousTarget = RenderCanvasPass(commandBuffer, previousCanvas, output, importsByHandle, previousTarget, submissionResources, depth: 0);
        currentTarget = RenderCanvasPass(commandBuffer, currentCanvas, output, importsByHandle, currentTarget, submissionResources, depth: 0);
        RenderOutputPass(commandBuffer, output, previousCanvas.Size, previousTarget, outputTarget, submissionResources);
        RenderOutputOverlayPass(commandBuffer, output, currentCanvas.Size, currentTarget, outputTarget, progress, submissionResources);
    }

    private VulkanOffscreenRenderTarget RenderCanvasPass(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        RenderOutputStateSnapshot output,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanSubmissionResourceScope submissionResources,
        int depth,
        IReadOnlyDictionary<DrawObjectId, VulkanOffscreenRenderTarget>? physicalBlurTargets = null)
    {
        var nestedTargets = RenderNestedCanvasTargets(
            commandBuffer,
            canvas,
            output,
            importsByHandle,
            submissionResources,
            depth);
        var effectResolutions = ResolveEffectsForCanvas(canvas);
        var blurredSourceTargets = physicalBlurTargets ?? RenderBlurredSourceTargets(
                commandBuffer,
                canvas,
                importsByHandle,
                submissionResources,
                effectResolutions);

        var currentTarget = canvasTarget;
        TransitionForColorAttachment(_vk, commandBuffer, currentTarget, currentTarget.CurrentLayout);
        currentTarget.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var background = canvas.BackgroundColor;
        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(background.R, background.G, background.B, background.A)
        };

        var framebuffer = BeginRenderPass(
            commandBuffer,
            currentTarget,
            canvas.Size,
            clearColor);
        submissionResources.RetainFramebuffer(framebuffer);

        var renderPassActive = true;
        try
        {
            foreach (var drawObject in canvas.Objects)
            {
                if (!drawObject.Enabled)
                {
                    continue;
                }

                if (drawObject is RenderAdjustmentLayerDrawObjectSnapshot adjustment)
                {
                    EndRenderPassInstance(commandBuffer);
                    renderPassActive = false;
                    TransitionToShaderRead(_vk, commandBuffer, currentTarget);
                    currentTarget = RenderAdjustmentEffects(
                        commandBuffer,
                        canvas,
                        adjustment,
                        currentTarget,
                        submissionResources);

                    TransitionForColorAttachment(_vk, commandBuffer, currentTarget, currentTarget.CurrentLayout);
                    currentTarget.CurrentLayout = ImageLayout.ColorAttachmentOptimal;
                    framebuffer = BeginRenderPass(
                        commandBuffer,
                        currentTarget,
                        canvas.Size,
                        clearColor,
                        _loadRenderPass);
                    submissionResources.RetainFramebuffer(framebuffer);
                    renderPassActive = true;
                    continue;
                }

                if (!TryValidateTransformAndCrop(drawObject, out var skipDraw))
                    continue;

                if (skipDraw)
                    continue;

                var effectResolution = effectResolutions[drawObject.Id];

                if (!IsSupportedBlendMode(drawObject))
                    continue;

                if (!effectResolution.Supported)
                    continue;

                if (drawObject is RenderSolidDrawObjectSnapshot solid)
                {
                    var solidPushConstants = CompositionPushConstantsBuilder.BuildSolid(solid);
                    DrawSolidLayer(
                        commandBuffer,
                        _solidPipeline,
                        solidPushConstants,
                        canvas.Size,
                        solid.Transform);
                    continue;
                }

                if (drawObject is RenderTextDrawObjectSnapshot textLayer)
                {
                    if (_fontAtlasBridge is null ||
                        !_fontAtlasBridge.TryResolveAtlas(
                            textLayer.Text,
                            textLayer.FontFamily,
                            textLayer.FontSize,
                            out _,
                            out var atlasImageView))
                    {
                        continue;
                    }

                    var textPushConstants = CompositionPushConstantsBuilder.BuildText(textLayer);
                    var textDescriptorSet = AllocateAndWriteDescriptorSet(atlasImageView);
                    submissionResources.RetainDescriptorSet(textDescriptorSet);

                    DrawTextLayer(
                        commandBuffer,
                        _textPipeline,
                        textDescriptorSet,
                        textPushConstants,
                        canvas.Size,
                        textLayer.Transform);
                    continue;
                }

                if (drawObject is RenderCanvasDrawObjectSnapshot nestedCanvas)
                {
                    if (!nestedTargets.TryGetValue(nestedCanvas, out var nestedTarget))
                    {
                        ReportNestedCanvasUnavailable(nestedCanvas);
                        continue;
                    }

                    var canvasPushConstants = CompositionPushConstantsBuilder.BuildCanvasComposite(nestedCanvas);
                    var canvasDescriptorSet = AllocateAndWriteDescriptorSet(nestedTarget.ImageView);
                    submissionResources.RetainDescriptorSet(canvasDescriptorSet);

                    DrawCanvasLayer(
                        commandBuffer,
                        _canvasCompositePipeline,
                        canvasDescriptorSet,
                        canvasPushConstants,
                        canvas.Size,
                        nestedCanvas.Transform);
                    continue;
                }

                if (drawObject is not RenderSourceLayerDrawObjectSnapshot sourceLayer)
                {
                    ReportUnsupportedDrawObject(drawObject);
                    continue;
                }

                if (effectResolution.SourceLayerEffects.Blur is not null)
                {
                    if (!blurredSourceTargets.TryGetValue(sourceLayer.Id, out var blurredTarget))
                        continue;

                    var blurredComposite = CreateBlurredSourceCompositeLayer(sourceLayer);
                    var blurredPushConstants = CompositionPushConstantsBuilder.BuildCanvasComposite(blurredComposite);
                    var blurredDescriptorSet = AllocateAndWriteDescriptorSet(blurredTarget.ImageView);
                    submissionResources.RetainDescriptorSet(blurredDescriptorSet);

                    DrawCanvasLayer(
                        commandBuffer,
                        _canvasCompositePipeline,
                        blurredDescriptorSet,
                        blurredPushConstants,
                        canvas.Size,
                        blurredComposite.Transform);
                    continue;
                }

                if (sourceLayer.BoundFrame?.Handle is not D3D11SharedTextureFrameHandle sharedHandle)
                {
                    continue;
                }

                if (!importsByHandle.TryGetValue(VulkanExternalTextureKey.From(sharedHandle), out var import))
                    continue;

                var frame = sourceLayer.BoundFrame!.Value;
                TransitionForShaderRead(commandBuffer, import);

                var pushConstants = CompositionPushConstantsBuilder.BuildSourceLayer(
                    sourceLayer,
                    frame,
                    effectResolution.SourceLayerEffects.ChromaKey,
                    effectResolution.SourceLayerEffects.ColorCorrection);
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
            if (renderPassActive)
                EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, currentTarget);
        return currentTarget;
    }

    private Dictionary<DrawObjectId, EffectResolution> ResolveEffectsForCanvas(RenderCanvasSnapshot canvas)
    {
        var resolutions = new Dictionary<DrawObjectId, EffectResolution>(canvas.Objects.Length);

        foreach (var drawObject in canvas.Objects)
        {
            SourceLayerEffectSelection sourceLayerEffects;
            bool supported;
            if (drawObject is RenderAdjustmentLayerDrawObjectSnapshot)
            {
                supported = true;
                sourceLayerEffects = default;
            }
            else
            {
                supported = TryResolveEffects(
                    drawObject,
                    allowSourceLayerEffects: drawObject is RenderSourceLayerDrawObjectSnapshot,
                    out sourceLayerEffects);
            }

            resolutions[drawObject.Id] = new EffectResolution(supported, sourceLayerEffects);
        }

        return resolutions;
    }

    private Dictionary<DrawObjectId, VulkanOffscreenRenderTarget> RenderBlurredSourceTargets(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyDictionary<DrawObjectId, EffectResolution> effectResolutions,
        IReadOnlyCollection<DrawObjectId>? drawObjectFilter = null)
    {
        var targets = new Dictionary<DrawObjectId, VulkanOffscreenRenderTarget>();

        foreach (var drawObject in canvas.Objects)
        {
            if (drawObject is not RenderSourceLayerDrawObjectSnapshot sourceLayer ||
                !sourceLayer.Enabled ||
                (drawObjectFilter is not null && !drawObjectFilter.Contains(sourceLayer.Id)) ||
                !effectResolutions.TryGetValue(sourceLayer.Id, out var resolution) ||
                !resolution.Supported ||
                resolution.SourceLayerEffects.Blur is not { } blur ||
                !CanPreRenderBlurSource(sourceLayer))
            {
                continue;
            }

            if (sourceLayer.BoundFrame?.Handle is not D3D11SharedTextureFrameHandle sharedHandle)
                continue;

            if (!importsByHandle.TryGetValue(VulkanExternalTextureKey.From(sharedHandle), out var import))
                continue;

            var localSize = ResolveLayerEffectTargetSize(sourceLayer);
            var sourceTarget = RentIntermediateTarget(canvas.PhysicalKey, sourceLayer.Id, salt: 1, localSize, submissionResources);
            var horizontalTarget = RentIntermediateTarget(canvas.PhysicalKey, sourceLayer.Id, salt: 2, localSize, submissionResources);
            var verticalTarget = RentIntermediateTarget(canvas.PhysicalKey, sourceLayer.Id, salt: 3, localSize, submissionResources);

            TransitionForShaderRead(commandBuffer, import);

            RenderSourceLayerToIntermediate(
                commandBuffer,
                localSize,
                sourceLayer,
                sourceLayer.BoundFrame.Value,
                import,
                sourceTarget,
                resolution.SourceLayerEffects,
                submissionResources);

            RenderBlurPass(
                commandBuffer,
                sourceTarget,
                horizontalTarget,
                blur.Radius,
                horizontal: true,
                submissionResources);

            RenderBlurPass(
                commandBuffer,
                horizontalTarget,
                verticalTarget,
                blur.Radius,
                horizontal: false,
                submissionResources);

            targets[sourceLayer.Id] = verticalTarget;
        }

        return targets;
    }

    private VulkanOffscreenRenderTarget RentIntermediateTarget(
        ResolvedCanvasKey canvasKey,
        DrawObjectId drawObjectId,
        byte salt,
        FrameSize size,
        VulkanSubmissionResourceScope submissionResources)
    {
        var handle = _intermediateTargetPool.Rent(
            canvasKey.Derive($"effect:{drawObjectId.Value:N}:{salt}"),
            size);
        submissionResources.RetainOffscreenTarget(handle);
        var target = (VulkanOffscreenRenderTarget)handle.Target;
        handle.Retire();
        return target;
    }

    private void RenderSourceLayerToIntermediate(
        CommandBuffer commandBuffer,
        FrameSize canvasSize,
        RenderSourceLayerDrawObjectSnapshot sourceLayer,
        GpuFrameReference frame,
        VulkanD3D11TextureImport import,
        VulkanOffscreenRenderTarget target,
        SourceLayerEffectSelection effects,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, target, target.CurrentLayout);
        target.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(0f, 0f, 0f, 0f)
        };

        var framebuffer = BeginRenderPass(commandBuffer, target, canvasSize, clearColor);
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var preBlurLayer = CreatePreBlurSourceLayer(sourceLayer, canvasSize);
            var pushConstants = CompositionPushConstantsBuilder.BuildSourceLayer(
                preBlurLayer,
                frame,
                effects.ChromaKey,
                effects.ColorCorrection);
            var descriptorSet = AllocateAndWriteDescriptorSet(import.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);

            DrawTexturedLayer(
                commandBuffer,
                _sourceLayerPipeline,
                descriptorSet,
                pushConstants,
                canvasSize,
                preBlurLayer.Transform);
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, target);
    }

    private void RenderBlurPass(
        CommandBuffer commandBuffer,
        VulkanOffscreenRenderTarget input,
        VulkanOffscreenRenderTarget output,
        float radius,
        bool horizontal,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, output, output.CurrentLayout);
        output.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(0f, 0f, 0f, 0f)
        };

        var framebuffer = BeginRenderPass(commandBuffer, output, output.Size, clearColor);
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var pushConstants = CompositionPushConstantsBuilder.BuildBlur(input.Size, radius, horizontal);
            var descriptorSet = AllocateAndWriteDescriptorSet(input.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);

            DrawFullscreen(
                commandBuffer,
                _blurPipeline,
                descriptorSet,
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref pushConstants, 1)),
                output.Size);
        }
        finally
        {
            EndRenderPassInstance(commandBuffer);
        }

        TransitionToShaderRead(_vk, commandBuffer, output);
    }

    private static bool CanPreRenderBlurSource(RenderSourceLayerDrawObjectSnapshot sourceLayer) =>
        sourceLayer.EffectiveCrop.IsValid &&
        IsFiniteTransform(sourceLayer.Transform) &&
        sourceLayer.Transform.HasPositiveSize;

    internal static FrameSize ResolveLayerEffectTargetSize(
        RenderSourceLayerDrawObjectSnapshot sourceLayer)
    {
        ArgumentNullException.ThrowIfNull(sourceLayer);
        return new FrameSize(
            Math.Max(1, (uint)Math.Ceiling(sourceLayer.Transform.Size.Width)),
            Math.Max(1, (uint)Math.Ceiling(sourceLayer.Transform.Size.Height)));
    }

    private static RenderSourceLayerDrawObjectSnapshot CreatePreBlurSourceLayer(
        RenderSourceLayerDrawObjectSnapshot sourceLayer,
        FrameSize localSize) =>
        new()
        {
            Id = sourceLayer.Id,
            Name = sourceLayer.Name,
            Enabled = sourceLayer.Enabled,
            Transform = new Transform2D
            {
                Size = new CanvasSize(localSize.Width, localSize.Height)
            },
            EffectiveCrop = sourceLayer.EffectiveCrop,
            Opacity = 1f,
            BlendMode = sourceLayer.BlendMode,
            Effects = sourceLayer.Effects,
            SourceEffects = sourceLayer.SourceEffects,
            SourceId = sourceLayer.SourceId,
            LayoutMode = sourceLayer.LayoutMode,
            LetterboxColor = sourceLayer.LetterboxColor,
            ContentRotationOverride = sourceLayer.ContentRotationOverride,
            BoundFrame = sourceLayer.BoundFrame
        };

    private static RenderCanvasDrawObjectSnapshot CreateBlurredSourceCompositeLayer(
        RenderSourceLayerDrawObjectSnapshot sourceLayer) =>
        new()
        {
            Id = sourceLayer.Id,
            Name = sourceLayer.Name,
            Enabled = sourceLayer.Enabled,
            Transform = sourceLayer.Transform,
            EffectiveCrop = NormalizedRect.Full,
            Opacity = sourceLayer.Opacity,
            BlendMode = sourceLayer.BlendMode,
            Effects = [],
            NestedCanvas = null
        };

    private Dictionary<RenderCanvasDrawObjectSnapshot, VulkanOffscreenRenderTarget> RenderNestedCanvasTargets(
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        RenderOutputStateSnapshot output,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        int depth)
    {
        var targets = new Dictionary<RenderCanvasDrawObjectSnapshot, VulkanOffscreenRenderTarget>();

        foreach (var drawObject in canvas.Objects)
        {
            if (drawObject is not RenderCanvasDrawObjectSnapshot nested ||
                !nested.Enabled ||
                nested.NestedCanvas is null ||
                nested.BlendMode != BlendMode.Normal ||
                HasEnabledEffects(nested) ||
                !nested.Transform.HasPositiveSize ||
                !VulkanLayerGeometry.TryCreate(nested.Transform, canvas.Size, out _))
            {
                continue;
            }

            if (depth >= MaxNestedCanvasDepth)
            {
                ReportNestedCanvasDepthExceeded(nested);
                continue;
            }

            var nestedHandle = _intermediateTargetPool.Rent(
                nested.NestedCanvas.PhysicalKey,
                nested.NestedCanvas.Size);
            submissionResources.RetainOffscreenTarget(nestedHandle);
            var nestedTarget = (VulkanOffscreenRenderTarget)nestedHandle.Target;
            nestedHandle.Retire();

            nestedTarget = RenderCanvasPass(
                commandBuffer,
                nested.NestedCanvas,
                output,
                importsByHandle,
                nestedTarget,
                submissionResources,
                depth + 1);

            targets[nested] = nestedTarget;
        }

        return targets;
    }

    private bool TryValidateTransformAndCrop(RenderDrawObjectSnapshot drawObject, out bool skipDraw)
    {
        skipDraw = false;
        var transform = drawObject.Transform;

        if (!IsFiniteTransform(transform) || !transform.HasPositiveSize)
        {
            ReportInvalidTransform(drawObject);
            skipDraw = true;
            return true;
        }

        if (!drawObject.EffectiveCrop.IsValid)
        {
            ReportInvalidCrop(drawObject);
            skipDraw = true;
            return true;
        }

        return true;
    }

    private static bool IsFiniteTransform(Transform2D transform) =>
        float.IsFinite(transform.Position.X) &&
        float.IsFinite(transform.Position.Y) &&
        float.IsFinite(transform.Size.Width) &&
        float.IsFinite(transform.Size.Height) &&
        float.IsFinite(transform.Pivot.X) &&
        float.IsFinite(transform.Pivot.Y) &&
        float.IsFinite(transform.RotationDegrees);

    private void ReportInvalidTransform(RenderDrawObjectSnapshot drawObject) =>
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.transform_invalid",
            $"Draw object '{drawObject.Name}' has invalid transform '{drawObject.Transform}' and was skipped.",
            nameof(VulkanCompositionShaderPipelines));

    private void ReportInvalidCrop(RenderDrawObjectSnapshot drawObject) =>
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.crop_invalid",
            $"Draw object '{drawObject.Name}' has invalid crop {drawObject.EffectiveCrop} and was skipped.",
            nameof(VulkanCompositionShaderPipelines));

    private bool IsSupportedBlendMode(RenderDrawObjectSnapshot drawObject)
    {
        if (drawObject.BlendMode == BlendMode.Normal)
            return true;

        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.blend_mode_unsupported",
            $"Draw object '{drawObject.Name}' uses unsupported blend mode '{drawObject.BlendMode}'.",
            nameof(VulkanCompositionShaderPipelines));
        return false;
    }

    private bool TryResolveEffects(
        RenderDrawObjectSnapshot drawObject,
        bool allowSourceLayerEffects,
        out SourceLayerEffectSelection sourceLayerEffects)
    {
        sourceLayerEffects = default;

        if (drawObject.Effects.IsDefaultOrEmpty)
            return true;

        var supported = true;
        ChromaKeyEffectSnapshot? chromaKey = null;
        ColorCorrectionEffectSnapshot? colorCorrection = null;
        BlurEffectSnapshot? blur = null;

        var sourcePlan = drawObject is RenderSourceLayerDrawObjectSnapshot sourceLayer
            ? CreateSourceEffectExecutionPlan(sourceLayer)
            : null;
        var layerPlan = CreateEffectExecutionPlan(drawObject);
        foreach (var effect in (sourcePlan?.OrderedEffects ?? []).Concat(layerPlan.OrderedEffects))
        {
            if (effect.Mask is not null)
            {
                ReportUnsupportedEffect(
                    drawObject,
                    effect,
                    "Masked effect composition requires the Vulkan mask-composite pipeline.");
                supported = false;
                continue;
            }

            if (allowSourceLayerEffects && effect is ChromaKeyEffectSnapshot chroma)
            {
                if (blur is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "ChromaKeyEffect must execute before BlurEffect in the current source layer path.");
                    supported = false;
                    continue;
                }

                if (chromaKey is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "Only one active ChromaKeyEffect is supported per source layer.");
                    supported = false;
                    continue;
                }

                if (!TryValidateChromaKey(drawObject, chroma))
                {
                    supported = false;
                    continue;
                }

                chromaKey = chroma;
                continue;
            }

            if (allowSourceLayerEffects && effect is ColorCorrectionEffectSnapshot color)
            {
                if (blur is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "ColorCorrectionEffect must execute before BlurEffect in the current source layer path.");
                    supported = false;
                    continue;
                }

                if (colorCorrection is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "Only one active ColorCorrectionEffect is supported per source layer.");
                    supported = false;
                    continue;
                }

                if (chromaKey is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "ColorCorrectionEffect must execute before ChromaKeyEffect in the current source layer shader.");
                    supported = false;
                    continue;
                }

                if (!TryValidateColorCorrection(drawObject, color))
                {
                    supported = false;
                    continue;
                }

                colorCorrection = color;
                continue;
            }

            if (allowSourceLayerEffects && effect is BlurEffectSnapshot blurEffect)
            {
                if (blur is not null)
                {
                    ReportUnsupportedEffect(
                        drawObject,
                        effect,
                        "Only one active BlurEffect is supported per source layer.");
                    supported = false;
                    continue;
                }

                if (!TryValidateBlur(drawObject, blurEffect))
                {
                    supported = false;
                    continue;
                }

                blur = blurEffect;
                continue;
            }

            ReportUnsupportedEffect(drawObject, effect);
            supported = false;
        }

        sourceLayerEffects = new SourceLayerEffectSelection(chromaKey, colorCorrection, blur);
        return supported;
    }

    internal static EffectExecutionPlan CreateEffectExecutionPlan(RenderDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(drawObject);
        return EffectExecutionPlanner.Default.CreatePlan(EffectScope.Layer, drawObject.Effects);
    }

    internal static EffectExecutionPlan CreateSourceEffectExecutionPlan(
        RenderSourceLayerDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(drawObject);
        return EffectExecutionPlanner.Default.CreatePlan(EffectScope.Source, drawObject.SourceEffects);
    }

    private static bool HasEnabledEffects(RenderDrawObjectSnapshot drawObject) =>
        !drawObject.Effects.IsDefaultOrEmpty &&
        drawObject.Effects.Any(effect => effect.Enabled);

    private bool TryValidateChromaKey(
        RenderDrawObjectSnapshot drawObject,
        ChromaKeyEffectSnapshot effect)
    {
        var valid = true;

        valid &= ValidateUnitRange(drawObject, effect, effect.Similarity, "Similarity");
        valid &= ValidateUnitRange(drawObject, effect, effect.Smoothness, "Smoothness");
        valid &= ValidateUnitRange(drawObject, effect, effect.SpillReduction, "SpillReduction");

        if (!effect.KeyColor.IsInRange())
        {
            ReportInvalidEffect(
                drawObject,
                effect,
                "KeyColor must contain finite color components in the [0,1] range.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateBlur(
        RenderDrawObjectSnapshot drawObject,
        BlurEffectSnapshot effect) =>
        ValidatePositiveFinite(drawObject, effect, effect.Radius, "Radius");

    private bool TryValidateColorCorrection(
        RenderDrawObjectSnapshot drawObject,
        ColorCorrectionEffectSnapshot effect)
    {
        var valid = true;

        valid &= ValidateFinite(drawObject, effect, effect.Brightness, "Brightness");
        valid &= ValidatePositiveFinite(drawObject, effect, effect.Contrast, "Contrast");
        valid &= ValidatePositiveFinite(drawObject, effect, effect.Saturation, "Saturation");
        valid &= ValidateFinite(drawObject, effect, effect.HueDegrees, "HueDegrees");

        return valid;
    }

    private bool ValidateUnitRange(
        RenderDrawObjectSnapshot drawObject,
        EffectStateSnapshot effect,
        float value,
        string fieldName)
    {
        if (float.IsFinite(value) && value >= 0f && value <= 1f)
            return true;

        ReportInvalidEffect(
            drawObject,
            effect,
            $"{fieldName} must be finite and in the [0,1] range.");
        return false;
    }

    private bool ValidatePositiveFinite(
        RenderDrawObjectSnapshot drawObject,
        EffectStateSnapshot effect,
        float value,
        string fieldName)
    {
        if (float.IsFinite(value) && value > 0f)
            return true;

        ReportInvalidEffect(
            drawObject,
            effect,
            $"{fieldName} must be a positive finite value.");
        return false;
    }

    private bool ValidateFinite(
        RenderDrawObjectSnapshot drawObject,
        EffectStateSnapshot effect,
        float value,
        string fieldName)
    {
        if (float.IsFinite(value))
            return true;

        ReportInvalidEffect(
            drawObject,
            effect,
            $"{fieldName} must be finite.");
        return false;
    }

    private void ReportUnsupportedEffect(
        RenderDrawObjectSnapshot drawObject,
        EffectStateSnapshot effect,
        string? reason = null)
    {
        var message = $"Draw object '{drawObject.Name}' uses unsupported effect '{effect.GetType().Name}'.";
        if (!string.IsNullOrWhiteSpace(reason))
            message += $" {reason}";

        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.effect_not_supported",
            message,
            nameof(VulkanCompositionShaderPipelines));
    }

    private void ReportInvalidEffect(
        RenderDrawObjectSnapshot drawObject,
        EffectStateSnapshot effect,
        string reason)
    {
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.effect_invalid",
            $"Draw object '{drawObject.Name}' has invalid effect '{effect.GetType().Name}'. {reason}",
            nameof(VulkanCompositionShaderPipelines));
    }

    private void ReportUnsupportedDrawObject(RenderDrawObjectSnapshot drawObject)
    {
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.drawobject_not_supported",
            $"Draw object '{drawObject.Name}' of type '{drawObject.GetType().Name}' is not supported by the Vulkan compositor yet.",
            nameof(VulkanCompositionShaderPipelines));
    }

    private void ReportNestedCanvasUnavailable(RenderCanvasDrawObjectSnapshot drawObject)
    {
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.canvas_not_available",
            $"Nested canvas draw object '{drawObject.Name}' does not have a renderable nested canvas.",
            nameof(VulkanCompositionShaderPipelines));
    }

    private void ReportNestedCanvasDepthExceeded(RenderCanvasDrawObjectSnapshot drawObject)
    {
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "render.canvas_depth_exceeded",
            $"Nested canvas draw object '{drawObject.Name}' exceeded the renderer nesting depth limit.",
            nameof(VulkanCompositionShaderPipelines));
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
            var pushConstants = CompositionPushConstantsBuilder.BuildOutputLetterbox(output, canvasSize);
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

    private void RenderOutputOverlayPass(
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        FrameSize canvasSize,
        VulkanOffscreenRenderTarget canvasTarget,
        VulkanOffscreenRenderTarget outputTarget,
        float opacity,
        VulkanSubmissionResourceScope submissionResources)
    {
        TransitionForColorAttachment(_vk, commandBuffer, outputTarget, outputTarget.CurrentLayout);
        outputTarget.CurrentLayout = ImageLayout.ColorAttachmentOptimal;

        var clearColor = new ClearValue
        {
            Color = new ClearColorValue(0f, 0f, 0f, 0f)
        };

        var framebuffer = BeginRenderPass(
            commandBuffer,
            outputTarget,
            output.OutputSize,
            clearColor,
            _loadRenderPass);
        submissionResources.RetainFramebuffer(framebuffer);

        try
        {
            var pushConstants = CompositionPushConstantsBuilder.BuildOutputLetterbox(output, canvasSize, opacity);
            var descriptorSet = AllocateAndWriteDescriptorSet(canvasTarget.ImageView);
            submissionResources.RetainDescriptorSet(descriptorSet);

            DrawFullscreen(
                commandBuffer,
                _outputLetterboxBlendPipeline,
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
        if (!VulkanLayerGeometry.TryCreate(transform, canvasSize, out var geometry))
            return;

        pushConstants.GeometryRect = geometry.GeometryRect;
        var viewport = geometry.Viewport;
        var scissor = geometry.Scissor;

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

    private void DrawTextLayer(
        CommandBuffer commandBuffer,
        Pipeline pipeline,
        DescriptorSet descriptorSet,
        MediaForgeTextPushConstants pushConstants,
        FrameSize canvasSize,
        Transform2D transform)
    {
        if (!VulkanLayerGeometry.TryCreate(transform, canvasSize, out var geometry))
            return;

        pushConstants.GeometryRect = geometry.GeometryRect;
        var viewport = geometry.Viewport;
        var scissor = geometry.Scissor;

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

    private void DrawSolidLayer(
        CommandBuffer commandBuffer,
        Pipeline pipeline,
        MediaForgeSolidPushConstants pushConstants,
        FrameSize canvasSize,
        Transform2D transform)
    {
        if (!VulkanLayerGeometry.TryCreate(transform, canvasSize, out var geometry))
            return;

        pushConstants.GeometryRect = geometry.GeometryRect;
        var viewport = geometry.Viewport;
        var scissor = geometry.Scissor;

        _vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);

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

    private void DrawCanvasLayer(
        CommandBuffer commandBuffer,
        Pipeline pipeline,
        DescriptorSet descriptorSet,
        MediaForgeCanvasCompositePushConstants pushConstants,
        FrameSize canvasSize,
        Transform2D transform)
    {
        if (!VulkanLayerGeometry.TryCreate(transform, canvasSize, out var geometry))
            return;

        pushConstants.GeometryRect = geometry.GeometryRect;
        var viewport = geometry.Viewport;
        var scissor = geometry.Scissor;

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
        ClearValue clearColor,
        RenderPass? renderPassOverride = null)
    {
        var renderPass = renderPassOverride ?? _renderPass;
        var framebuffer = CreateFramebuffer(renderPass, target.ImageView, extent);

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
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

    private Framebuffer CreateFramebuffer(RenderPass renderPass, ImageView imageView, FrameSize extent)
    {
        var attachment = imageView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
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

    private DescriptorSet AllocateAndWriteDescriptorSet(ImageView originalImageView, ImageView effectImageView)
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

        var imageInfos = stackalloc DescriptorImageInfo[2]
        {
            new DescriptorImageInfo
            {
                Sampler = _sampler,
                ImageView = originalImageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            },
            new DescriptorImageInfo
            {
                Sampler = _sampler,
                ImageView = effectImageView,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal
            }
        };
        var writes = stackalloc WriteDescriptorSet[2]
        {
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 0,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfos[0]
            },
            new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = 1,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfos[1]
            }
        };

        _vk.UpdateDescriptorSets(_deviceHandle, 2, writes, 0, null);
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

        _intermediateTargetPool.Dispose();

        if (_sourceLayerPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _sourceLayerPipeline, null);

        if (_solidPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _solidPipeline, null);

        if (_canvasCompositePipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _canvasCompositePipeline, null);

        if (_outputLetterboxPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _outputLetterboxPipeline, null);

        if (_outputLetterboxBlendPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _outputLetterboxBlendPipeline, null);

        if (_textPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _textPipeline, null);

        if (_blurPipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _blurPipeline, null);

        if (_maskCompositePipeline.Handle != 0)
            _vk.DestroyPipeline(_deviceHandle, _maskCompositePipeline, null);

        if (_pipelineLayout.Handle != 0)
            _vk.DestroyPipelineLayout(_deviceHandle, _pipelineLayout, null);

        if (_renderPass.Handle != 0)
            _vk.DestroyRenderPass(_deviceHandle, _renderPass, null);

        if (_loadRenderPass.Handle != 0)
            _vk.DestroyRenderPass(_deviceHandle, _loadRenderPass, null);

        if (_descriptorPool.Handle != 0)
            _vk.DestroyDescriptorPool(_deviceHandle, _descriptorPool, null);

        if (_descriptorSetLayout.Handle != 0)
            _vk.DestroyDescriptorSetLayout(_deviceHandle, _descriptorSetLayout, null);

        if (_sampler.Handle != 0)
            _vk.DestroySampler(_deviceHandle, _sampler, null);

        DestroyShaderModule(_sourceLayerFragmentModule);
        DestroyShaderModule(_solidFragmentModule);
        DestroyShaderModule(_canvasCompositeFragmentModule);
        DestroyShaderModule(_outputLetterboxFragmentModule);
        DestroyShaderModule(_textFragmentModule);
        DestroyShaderModule(_blurFragmentModule);
        DestroyShaderModule(_maskCompositeFragmentModule);
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
        var samplerBindings = stackalloc DescriptorSetLayoutBinding[2]
        {
            new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit
            },
            new DescriptorSetLayoutBinding
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
            PBindings = samplerBindings
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
            DescriptorCount = MaxDescriptorSetsPerSubmit * 2
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = MaxDescriptorSetsPerSubmit,
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

    private RenderPass CreateRenderPass(AttachmentLoadOp loadOp)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = Format.R8G8B8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = loadOp,
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

    private readonly struct EffectResolution(
        bool supported,
        SourceLayerEffectSelection sourceLayerEffects)
    {
        public bool Supported { get; } = supported;

        public SourceLayerEffectSelection SourceLayerEffects { get; } = sourceLayerEffects;
    }

    private readonly struct SourceLayerEffectSelection(
        ChromaKeyEffectSnapshot? chromaKey,
        ColorCorrectionEffectSnapshot? colorCorrection,
        BlurEffectSnapshot? blur)
    {
        public ChromaKeyEffectSnapshot? ChromaKey { get; } = chromaKey;

        public ColorCorrectionEffectSnapshot? ColorCorrection { get; } = colorCorrection;

        public BlurEffectSnapshot? Blur { get; } = blur;
    }
}
