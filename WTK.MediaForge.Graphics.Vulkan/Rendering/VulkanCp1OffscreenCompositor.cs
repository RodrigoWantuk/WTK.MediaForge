using Silk.NET.Vulkan;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanCp1OffscreenCompositor
{
    public static void Compose(
        VulkanCp1ShaderPipelines pipelines,
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

        foreach (var output in snapshot.Outputs)
        {
            if (!offscreenTargets.TryGetValue(output.Id, out var targetHandle) || !targetHandle.IsAlive)
                continue;

            if (targetHandle.Target is not VulkanOffscreenRenderTarget outputTarget)
                continue;

            var canvas = snapshot.Canvases.FirstOrDefault(c => c.Id == output.CanvasId);
            if (canvas is null)
                continue;

            var hasDrawableLayer = canvas.Objects
                .OfType<RenderSourceLayerDrawObjectSnapshot>()
                .Any(layer => layer.Enabled &&
                              layer.BoundFrame?.Handle is D3D11SharedTextureFrameHandle sharedHandle &&
                              importsByHandle.ContainsKey(VulkanExternalTextureKey.From(sharedHandle)));

            if (!hasDrawableLayer)
                continue;

            submissionResources.RetainOffscreenTarget(targetHandle);

            pipelines.ComposeOutput(
                commandBuffer,
                output,
                canvas,
                importsByHandle,
                outputTarget,
                submissionResources);
        }
    }
}
