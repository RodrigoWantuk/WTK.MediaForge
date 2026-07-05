using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed class RenderFrameSnapshot : IDisposable
{
    private int _disposed;

    public long ProjectStateVersion { get; init; }

    public ImmutableArray<RenderCanvasSnapshot> Canvases { get; init; } =
        ImmutableArray<RenderCanvasSnapshot>.Empty;

    public ImmutableArray<RenderOutputStateSnapshot> Outputs { get; init; } =
        ImmutableArray<RenderOutputStateSnapshot>.Empty;

    public ImmutableArray<GpuFrameLease> FrameLeases { get; init; } =
        ImmutableArray<GpuFrameLease>.Empty;

    public RenderFrameContext Context { get; init; }

    internal IMediaForgeDiagnosticsSink? Diagnostics { get; init; }

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
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    Diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "render.lease_release_failed",
                    "Failed to release GPU frame lease during snapshot dispose.",
                    nameof(RenderFrameSnapshot),
                    ex);
            }
        }
    }
}
