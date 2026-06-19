using System.Collections.Immutable;
using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Composition.Snapshots;

public sealed class RenderFrameSnapshot : IDisposable
{
    private int _disposed;

    public long ProjectStateVersion { get; init; }

    public ImmutableArray<RenderCanvasSnapshot> Canvases { get; init; } =
        ImmutableArray<RenderCanvasSnapshot>.Empty;

    public ImmutableArray<RenderOutputStateSnapshot> Outputs { get; init; } =
        ImmutableArray<RenderOutputStateSnapshot>.Empty;

    public ImmutableArray<GpuFrameLease> FrameLeases { get; init; } =
        ImmutableArray<GpuFrameLease>.Empty;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var lease in FrameLeases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception)
            {
                // TODO: Diagnostics.Record lease release failure.
            }
        }
    }
}
