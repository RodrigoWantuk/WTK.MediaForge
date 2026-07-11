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

        foreach (var output in snapshot.Outputs)
        {
            if (!offscreenTargets.TryGetValue(output.Id, out var targetHandle) || !targetHandle.IsAlive)
                continue;

            if (targetHandle.Target is not VulkanOffscreenRenderTarget outputTarget)
                continue;

            var canvas = snapshot.Canvases.FirstOrDefault(c => c.Id == output.CanvasId);
            if (canvas is null)
                continue;

            submissionResources.RetainOffscreenTarget(targetHandle);

            if (output.RouteTransitionKind == OutputRouteTransitionKind.Fade &&
                output.PreviousCanvasId is { } previousCanvasId &&
                snapshot.Canvases.FirstOrDefault(c => c.Id == previousCanvasId) is { } previousCanvas)
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
}
