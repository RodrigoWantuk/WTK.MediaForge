using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed record VulkanOffscreenCompositionResult(
    IReadOnlyList<IRenderedOutputSurfaceLease> Surfaces,
    VulkanPhysicalCompositionStats Stats);

internal sealed record VulkanPhysicalCompositionStats(
    int CanvasRenderPasses,
    int ReusedCanvasPasses,
    int OutputCompositePasses,
    int TransitionPasses,
    int EffectIntermediatePasses)
{
    public static VulkanPhysicalCompositionStats Empty { get; } = new(0, 0, 0, 0, 0);
}

internal static class VulkanOffscreenCompositor
{
    public static VulkanOffscreenCompositionResult Compose(
        VulkanCompositionShaderPipelines pipelines,
        CommandBuffer commandBuffer,
        RenderFrameSnapshot snapshot,
        IReadOnlyDictionary<RenderOutputId, VulkanOffscreenTargetHandle> offscreenTargets,
        IReadOnlyList<VulkanExternalTextureLease> textureLeases,
        VulkanSubmissionResourceScope submissionResources)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(offscreenTargets);
        ArgumentNullException.ThrowIfNull(textureLeases);

        var importsByHandle = textureLeases.ToDictionary(
            lease => VulkanExternalTextureKey.From(lease.Import.SourceHandle),
            lease => lease.Import);
        var renderedSurfaces = new List<IRenderedOutputSurfaceLease>();
        var physicalPlan = ResolvePhysicalPlan(snapshot);
        var outputsById = snapshot.Outputs.ToDictionary(static output => output.Id);
        var canvasesById = snapshot.Canvases.ToDictionary(static canvas => canvas.Id);
        var operationsByKey = physicalPlan.Operations.ToDictionary(
            static operation => operation.Key,
            StringComparer.Ordinal);
        var canvasCache = new Dictionary<string, RenderedCanvasTarget>(StringComparer.Ordinal);
        var stats = new PhysicalCompositionStatsBuilder();

        foreach (var operation in physicalPlan.Operations.Where(static operation =>
                     operation.Kind == PhysicalRenderGraphOperationKind.RenderOutput))
        {
            if (operation.OutputId is not { } outputId ||
                !outputsById.TryGetValue(outputId, out var output))
            {
                continue;
            }

            if (!offscreenTargets.TryGetValue(output.Id, out var targetHandle) || !targetHandle.IsAlive)
                continue;

            if (targetHandle.Target is not VulkanOffscreenRenderTarget outputTarget)
                continue;

            var dependency = ResolveOutputDependency(operation, operationsByKey);
            var canvasId = dependency?.CanvasId ?? operation.CanvasId ?? output.CanvasId;
            if (!canvasesById.TryGetValue(canvasId, out var canvas))
                continue;

            submissionResources.RetainOffscreenTarget(targetHandle);

            var composed = dependency?.Kind == PhysicalRenderGraphOperationKind.RenderOutputTransition &&
                output.RouteTransitionKind == OutputRouteTransitionKind.Fade
                    ? TryComposeTransitionOutput(
                        pipelines,
                        commandBuffer,
                        output,
                        dependency,
                        operationsByKey,
                        canvasesById,
                        importsByHandle,
                        outputTarget,
                        submissionResources,
                        canvasCache,
                        stats)
                    : TryComposeOutput(
                        pipelines,
                        commandBuffer,
                        output,
                        dependency,
                        canvasId,
                        operationsByKey,
                        canvasesById,
                        importsByHandle,
                        outputTarget,
                        submissionResources,
                        canvasCache,
                        stats);

            if (!composed)
                continue;

            renderedSurfaces.Add(new VulkanRenderedOutputSurfaceLease(
                targetHandle,
                output.Id,
                outputTarget.Size,
                RenderPixelFormat.Rgba8Unorm));
        }

        return new VulkanOffscreenCompositionResult(renderedSurfaces, stats.Build());
    }

    private static bool TryComposeOutput(
        VulkanCompositionShaderPipelines pipelines,
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        PhysicalRenderGraphOperation? dependency,
        CanvasId canvasId,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        IReadOnlyDictionary<CanvasId, RenderCanvasSnapshot> canvasesById,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats)
    {
        var canvasOperation = dependency?.Kind == PhysicalRenderGraphOperationKind.RenderCanvas
            ? dependency
            : ResolveCanvasOperation(dependency, canvasId, operationsByKey);

        if (!TryGetOrRenderCanvasTarget(
                pipelines,
                commandBuffer,
                output,
                canvasOperation,
                canvasId,
                canvasesById,
                importsByHandle,
                submissionResources,
                operationsByKey,
                canvasCache,
                stats,
                out var renderedCanvas))
        {
            return false;
        }

        pipelines.ComposeOutputFromCanvasTarget(
            commandBuffer,
            output,
            renderedCanvas.Size,
            renderedCanvas.Target,
            outputTarget,
            submissionResources);
        stats.RecordOutputCompositePass();
        return true;
    }

    private static bool TryComposeTransitionOutput(
        VulkanCompositionShaderPipelines pipelines,
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        PhysicalRenderGraphOperation transitionOperation,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        IReadOnlyDictionary<CanvasId, RenderCanvasSnapshot> canvasesById,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats)
    {
        var currentCanvasId = transitionOperation.CanvasId ?? output.CanvasId;
        var previousCanvasId = transitionOperation.PreviousCanvasId ?? output.PreviousCanvasId;
        if (previousCanvasId is not { } previousId)
            return false;

        var progress = Math.Clamp(output.RouteTransitionProgress, 0f, 1f);
        var previousOperation = ResolveCanvasOperation(transitionOperation, previousId, operationsByKey);
        var currentOperation = ResolveCanvasOperation(transitionOperation, currentCanvasId, operationsByKey);

        stats.RecordTransitionPass();

        if (progress <= 0f)
        {
            return TryComposeOutput(
                pipelines,
                commandBuffer,
                output,
                previousOperation,
                previousId,
                operationsByKey,
                canvasesById,
                importsByHandle,
                outputTarget,
                submissionResources,
                canvasCache,
                stats);
        }

        if (progress >= 1f)
        {
            return TryComposeOutput(
                pipelines,
                commandBuffer,
                output,
                currentOperation,
                currentCanvasId,
                operationsByKey,
                canvasesById,
                importsByHandle,
                outputTarget,
                submissionResources,
                canvasCache,
                stats);
        }

        if (!TryGetOrRenderCanvasTarget(
                pipelines,
                commandBuffer,
                output,
                previousOperation,
                previousId,
                canvasesById,
                importsByHandle,
                submissionResources,
                operationsByKey,
                canvasCache,
                stats,
                out var previousCanvas) ||
            !TryGetOrRenderCanvasTarget(
                pipelines,
                commandBuffer,
                output,
                currentOperation,
                currentCanvasId,
                canvasesById,
                importsByHandle,
                submissionResources,
                operationsByKey,
                canvasCache,
                stats,
                out var currentCanvas))
        {
            return false;
        }

        pipelines.ComposeOutputFromCanvasTarget(
            commandBuffer,
            output,
            previousCanvas.Size,
            previousCanvas.Target,
            outputTarget,
            submissionResources);
        pipelines.ComposeOutputOverlayFromCanvasTarget(
            commandBuffer,
            output,
            currentCanvas.Size,
            currentCanvas.Target,
            outputTarget,
            progress,
            submissionResources);
        stats.RecordOutputCompositePass();
        stats.RecordOutputCompositePass();
        return true;
    }

    private static bool TryGetOrRenderCanvasTarget(
        VulkanCompositionShaderPipelines pipelines,
        CommandBuffer commandBuffer,
        RenderOutputStateSnapshot output,
        PhysicalRenderGraphOperation? canvasOperation,
        CanvasId canvasId,
        IReadOnlyDictionary<CanvasId, RenderCanvasSnapshot> canvasesById,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats,
        [NotNullWhen(true)] out RenderedCanvasTarget? renderedCanvas)
    {
        renderedCanvas = null;
        if (!canvasesById.TryGetValue(canvasId, out var canvas))
            return false;

        var cacheKey = canvasOperation?.Key ?? $"canvas:{canvas.Id}:snapshot:{canvas.Size.Width}x{canvas.Size.Height}";
        if (canvasCache.TryGetValue(cacheKey, out renderedCanvas))
        {
            stats.RecordReusedCanvasPass();
            return true;
        }

        var physicalBlurTargets = RenderEffectIntermediateTargetsForCanvas(
            pipelines,
            commandBuffer,
            canvas,
            canvasOperation,
            importsByHandle,
            submissionResources,
            operationsByKey,
            stats);

        var target = pipelines.RenderCanvasToIntermediateTarget(
            commandBuffer,
            canvas,
            output,
            importsByHandle,
            submissionResources,
            physicalBlurTargets);

        renderedCanvas = new RenderedCanvasTarget(target, canvas.Size);
        canvasCache.Add(cacheKey, renderedCanvas);
        stats.RecordCanvasRenderPass();
        return true;
    }

    private static IReadOnlyDictionary<DrawObjectId, VulkanOffscreenRenderTarget>? RenderEffectIntermediateTargetsForCanvas(
        VulkanCompositionShaderPipelines pipelines,
        CommandBuffer commandBuffer,
        RenderCanvasSnapshot canvas,
        PhysicalRenderGraphOperation? canvasOperation,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        PhysicalCompositionStatsBuilder stats)
    {
        if (canvasOperation is null)
            return null;

        var drawObjectIds = canvasOperation.Dependencies
            .Select(dependencyKey => operationsByKey.TryGetValue(dependencyKey, out var dependency)
                ? dependency
                : null)
            .Where(static dependency =>
                dependency?.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate &&
                dependency.CanvasId is not null &&
                dependency.DrawObjectId is not null)
            .Where(dependency => dependency!.CanvasId == canvas.Id)
            .Select(dependency => dependency!.DrawObjectId!.Value)
            .Distinct()
            .ToArray();

        if (drawObjectIds.Length == 0)
            return new Dictionary<DrawObjectId, VulkanOffscreenRenderTarget>();

        var targets = pipelines.RenderBlurEffectIntermediateTargets(
            commandBuffer,
            canvas,
            importsByHandle,
            submissionResources,
            drawObjectIds);
        stats.RecordEffectIntermediatePasses(targets.Count);
        return targets;
    }

    private static PhysicalRenderGraphOperation? ResolveCanvasOperation(
        PhysicalRenderGraphOperation? operation,
        CanvasId canvasId,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey)
    {
        if (operation?.Kind == PhysicalRenderGraphOperationKind.RenderCanvas &&
            operation.CanvasId == canvasId)
        {
            return operation;
        }

        if (operation is null)
            return null;

        foreach (var dependencyKey in operation.Dependencies)
        {
            if (!operationsByKey.TryGetValue(dependencyKey, out var dependency))
                continue;

            if (dependency.Kind == PhysicalRenderGraphOperationKind.RenderCanvas &&
                dependency.CanvasId == canvasId)
            {
                return dependency;
            }

            if (dependency.Kind == PhysicalRenderGraphOperationKind.RenderOutputTransition &&
                ResolveCanvasOperation(dependency, canvasId, operationsByKey) is { } nestedCanvasOperation)
            {
                return nestedCanvasOperation;
            }
        }

        return null;
    }

    private static PhysicalRenderGraphPlan ResolvePhysicalPlan(RenderFrameSnapshot snapshot) =>
        snapshot.RenderGraphExecution?.PhysicalPlan ?? MediaForgeRenderGraphCompiler.Compile(snapshot).PhysicalPlan;

    private static PhysicalRenderGraphOperation? ResolveOutputDependency(
        PhysicalRenderGraphOperation outputOperation,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey)
    {
        foreach (var dependencyKey in outputOperation.Dependencies)
        {
            if (operationsByKey.TryGetValue(dependencyKey, out var dependency))
                return dependency;
        }

        return null;
    }

    private sealed record RenderedCanvasTarget(VulkanOffscreenRenderTarget Target, FrameSize Size);

    private sealed class PhysicalCompositionStatsBuilder
    {
        private int _canvasRenderPasses;
        private int _reusedCanvasPasses;
        private int _outputCompositePasses;
        private int _transitionPasses;
        private int _effectIntermediatePasses;

        public void RecordCanvasRenderPass() => _canvasRenderPasses++;

        public void RecordReusedCanvasPass() => _reusedCanvasPasses++;

        public void RecordOutputCompositePass() => _outputCompositePasses++;

        public void RecordTransitionPass() => _transitionPasses++;

        public void RecordEffectIntermediatePasses(int count) => _effectIntermediatePasses += count;

        public VulkanPhysicalCompositionStats Build() =>
            new(_canvasRenderPasses, _reusedCanvasPasses, _outputCompositePasses, _transitionPasses, _effectIntermediatePasses);
    }
}
