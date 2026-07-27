using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class RenderFrameSnapshotGpuFrames
{
    public static IReadOnlyList<D3D11SharedTextureFrameHandle> CollectD3D11SharedTextures(
        RenderFrameSnapshot snapshot,
        IReadOnlySet<SourceId>? acquiredSourceIds = null)
    {
        var handles = new List<D3D11SharedTextureFrameHandle>();
        var seen = new HashSet<VulkanExternalTextureKey>();

        foreach (var canvas in snapshot.Canvases)
            Collect(canvas.Objects, handles, seen, acquiredSourceIds);

        return handles;
    }

    private static void Collect(
        IReadOnlyList<RenderDrawObjectSnapshot> objects,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<VulkanExternalTextureKey> seen,
        IReadOnlySet<SourceId>? acquiredSourceIds)
    {
        foreach (var drawObject in objects)
        {
            switch (drawObject)
            {
                case RenderSourceLayerDrawObjectSnapshot sourceLayer
                    when sourceLayer.Enabled &&
                         (acquiredSourceIds is null || acquiredSourceIds.Contains(sourceLayer.SourceId)) &&
                         sourceLayer.BoundFrame?.Handle is D3D11SharedTextureFrameHandle handle:
                    TryAdd(handle, handles, seen);
                    break;

                case RenderCanvasDrawObjectSnapshot canvasDraw
                    when canvasDraw.Enabled && canvasDraw.NestedCanvas is not null:
                    Collect(canvasDraw.NestedCanvas.Objects, handles, seen, acquiredSourceIds);
                    break;
            }
        }
    }

    private static void TryAdd(
        D3D11SharedTextureFrameHandle handle,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<VulkanExternalTextureKey> seen)
    {
        if (!handle.HasSharedHandle)
            return;

        var key = VulkanExternalTextureKey.From(handle);

        if (!seen.Add(key))
            return;

        handles.Add(handle);
    }
}
