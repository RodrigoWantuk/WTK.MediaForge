using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanOffscreenCompositor
{
    public static IReadOnlyList<IRenderedOutputSurfaceLease> Compose(
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

            if (ShouldComposeTransition(output, dependency, canvasesById, out var previousCanvas))
            {
                pipelines.ComposeTransitionOutput(
                    commandBuffer,
                    output,
                    previousCanvas,
                    canvas,
                    importsByHandle,
                    outputTarget,
                    submissionResources);
            }
            else
            {
                pipelines.ComposeOutput(
                    commandBuffer,
                    output,
                    canvas,
                    importsByHandle,
                    outputTarget,
                    submissionResources);
            }

            renderedSurfaces.Add(new VulkanRenderedOutputSurfaceLease(
                targetHandle,
                output.Id,
                outputTarget.Size,
                RenderPixelFormat.Rgba8Unorm));
        }

        return renderedSurfaces;
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

    private static bool ShouldComposeTransition(
        RenderOutputStateSnapshot output,
        PhysicalRenderGraphOperation? dependency,
        IReadOnlyDictionary<CanvasId, RenderCanvasSnapshot> canvasesById,
        [NotNullWhen(true)] out RenderCanvasSnapshot? previousCanvas)
    {
        previousCanvas = null;

        if (dependency?.Kind != PhysicalRenderGraphOperationKind.RenderOutputTransition ||
            output.RouteTransitionKind != OutputRouteTransitionKind.Fade)
        {
            return false;
        }

        var previousCanvasId = dependency.PreviousCanvasId ?? output.PreviousCanvasId;
        if (previousCanvasId is not { } previousId ||
            !canvasesById.TryGetValue(previousId, out previousCanvas))
        {
            return false;
        }

        return true;
    }
}
