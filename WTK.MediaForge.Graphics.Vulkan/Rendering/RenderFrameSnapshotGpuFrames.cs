using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class RenderFrameSnapshotGpuFrames
{
    public static IReadOnlyList<D3D11SharedTextureFrameHandle> CollectD3D11SharedTextures(RenderFrameSnapshot snapshot)
    {
        var handles = new List<D3D11SharedTextureFrameHandle>();
        var seen = new HashSet<nint>();

        foreach (var canvas in snapshot.Canvases)
            Collect(canvas.Objects, handles, seen);

        return handles;
    }

    private static void Collect(
        IReadOnlyList<RenderDrawObjectSnapshot> objects,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<nint> seen)
    {
        foreach (var drawObject in objects)
        {
            switch (drawObject)
            {
                case RenderSourceLayerDrawObjectSnapshot sourceLayer
                    when sourceLayer.Enabled && sourceLayer.BoundFrame?.Handle is D3D11SharedTextureFrameHandle handle:
                    TryAdd(handle, handles, seen);
                    break;

                case RenderCanvasDrawObjectSnapshot canvasDraw
                    when canvasDraw.Enabled && canvasDraw.NestedCanvas is not null:
                    Collect(canvasDraw.NestedCanvas.Objects, handles, seen);
                    break;
            }
        }
    }

    private static void TryAdd(
        D3D11SharedTextureFrameHandle handle,
        List<D3D11SharedTextureFrameHandle> handles,
        HashSet<nint> seen)
    {
        if (!handle.HasSharedHandle || !seen.Add(handle.SharedHandle.DangerousGetHandleForInterop()))
            return;

        handles.Add(handle);
    }
}
