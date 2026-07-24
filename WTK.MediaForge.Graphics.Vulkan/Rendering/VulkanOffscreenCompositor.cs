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
        PhysicalRenderGraphPlan physicalPlan,
        IReadOnlyDictionary<RenderOutputId, VulkanOffscreenTargetHandle> offscreenTargets,
        IReadOnlyList<VulkanExternalTextureLease> textureLeases,
        VulkanSubmissionResourceScope submissionResources)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(physicalPlan);
        ArgumentNullException.ThrowIfNull(offscreenTargets);
        ArgumentNullException.ThrowIfNull(textureLeases);

        var importsByHandle = textureLeases.ToDictionary(
            lease => VulkanExternalTextureKey.From(lease.Import.SourceHandle),
            lease => lease.Import);
        var renderedSurfaces = new List<IRenderedOutputSurfaceLease>();
        var outputsById = snapshot.Outputs.ToDictionary(static output => output.Id);
        var canvasesByKey = snapshot.Canvases.ToDictionary(static canvas => canvas.PhysicalKey);
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
            var canvasKey = dependency?.ResolvedCanvasKey ?? operation.ResolvedCanvasKey ?? output.EffectiveResolvedCanvasKey;
            if (canvasKey.IsEmpty || !canvasesByKey.ContainsKey(canvasKey))
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
                        canvasesByKey,
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
                        canvasKey,
                        operationsByKey,
                        canvasesByKey,
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
        ResolvedCanvasKey canvasKey,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        IReadOnlyDictionary<ResolvedCanvasKey, RenderCanvasSnapshot> canvasesByKey,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats)
    {
        var canvasOperation = dependency?.Kind is PhysicalRenderGraphOperationKind.RenderCanvas or PhysicalRenderGraphOperationKind.RenderCanvasEffect
            ? dependency
            : ResolveCanvasOperation(dependency, canvasKey, operationsByKey);

        if (!TryGetOrRenderCanvasTarget(
                pipelines,
                commandBuffer,
                output,
                canvasOperation,
                canvasKey,
                canvasesByKey,
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
        IReadOnlyDictionary<ResolvedCanvasKey, RenderCanvasSnapshot> canvasesByKey,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanOffscreenRenderTarget outputTarget,
        VulkanSubmissionResourceScope submissionResources,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats)
    {
        var currentCanvasKey = transitionOperation.ResolvedCanvasKey ?? output.EffectiveResolvedCanvasKey;
        var previousCanvasKey = transitionOperation.PreviousResolvedCanvasKey ?? output.PreviousResolvedCanvasKey;
        if (currentCanvasKey.IsEmpty || previousCanvasKey is not { IsEmpty: false } previousKey)
            return false;

        var progress = Math.Clamp(output.RouteTransitionProgress, 0f, 1f);
        var previousOperation = ResolveCanvasOperation(transitionOperation, previousKey, operationsByKey);
        var currentOperation = ResolveCanvasOperation(transitionOperation, currentCanvasKey, operationsByKey);

        stats.RecordTransitionPass();

        if (progress <= 0f)
        {
            return TryComposeOutput(
                pipelines,
                commandBuffer,
                output,
                previousOperation,
                previousKey,
                operationsByKey,
                canvasesByKey,
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
                currentCanvasKey,
                operationsByKey,
                canvasesByKey,
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
                previousKey,
                canvasesByKey,
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
                currentCanvasKey,
                canvasesByKey,
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
        ResolvedCanvasKey canvasKey,
        IReadOnlyDictionary<ResolvedCanvasKey, RenderCanvasSnapshot> canvasesByKey,
        IReadOnlyDictionary<VulkanExternalTextureKey, VulkanD3D11TextureImport> importsByHandle,
        VulkanSubmissionResourceScope submissionResources,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey,
        Dictionary<string, RenderedCanvasTarget> canvasCache,
        PhysicalCompositionStatsBuilder stats,
        [NotNullWhen(true)] out RenderedCanvasTarget? renderedCanvas)
    {
        renderedCanvas = null;
        if (!canvasesByKey.TryGetValue(canvasKey, out var canvas))
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

        var drawObjectIds = CollectEffectDependencies(canvasOperation, operationsByKey)
            .Where(static dependency =>
                dependency.Kind == PhysicalRenderGraphOperationKind.RenderEffectIntermediate &&
                dependency.ResolvedCanvasKey is not null &&
                dependency.DrawObjectId is not null)
            .Where(dependency => dependency.ResolvedCanvasKey == canvas.PhysicalKey)
            .Select(dependency => dependency.DrawObjectId!.Value)
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

    private static IEnumerable<PhysicalRenderGraphOperation> CollectEffectDependencies(
        PhysicalRenderGraphOperation operation,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey)
    {
        var pending = new Stack<string>(operation.Dependencies.Reverse());
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var key))
        {
            if (!visited.Add(key) || !operationsByKey.TryGetValue(key, out var dependency))
                continue;

            yield return dependency;
            foreach (var nestedDependency in dependency.Dependencies.Reverse())
                pending.Push(nestedDependency);
        }
    }

    private static PhysicalRenderGraphOperation? ResolveCanvasOperation(
        PhysicalRenderGraphOperation? operation,
        ResolvedCanvasKey canvasKey,
        IReadOnlyDictionary<string, PhysicalRenderGraphOperation> operationsByKey)
    {
        if ((operation?.Kind is PhysicalRenderGraphOperationKind.RenderCanvas or PhysicalRenderGraphOperationKind.RenderCanvasEffect) &&
            operation.ResolvedCanvasKey == canvasKey)
        {
            return operation;
        }

        if (operation is null)
            return null;

        foreach (var dependencyKey in operation.Dependencies)
        {
            if (!operationsByKey.TryGetValue(dependencyKey, out var dependency))
                continue;

            if ((dependency.Kind is PhysicalRenderGraphOperationKind.RenderCanvas or PhysicalRenderGraphOperationKind.RenderCanvasEffect) &&
                dependency.ResolvedCanvasKey == canvasKey)
            {
                return dependency;
            }

            if (ResolveCanvasOperation(dependency, canvasKey, operationsByKey) is { } nestedCanvasOperation)
            {
                return nestedCanvasOperation;
            }
        }

        return null;
    }

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
