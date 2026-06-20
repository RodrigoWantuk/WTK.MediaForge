using System.Collections.Immutable;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Snapshots;

internal sealed class SnapshotBuildResult : IDisposable
{
    private int _disposed;
    private RenderFrameSnapshot? _snapshot;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;

    private SnapshotBuildResult(
        RenderFrameSnapshot snapshot,
        ImmutableArray<SnapshotDiagnostic> diagnostics,
        IMediaForgeDiagnosticsSink? diagnosticsSink)
    {
        _snapshot = snapshot;
        Diagnostics = diagnostics;
        _diagnostics = diagnosticsSink;
    }

    public RenderFrameSnapshot? Snapshot => _snapshot;

    public ImmutableArray<SnapshotDiagnostic> Diagnostics { get; }

    public static SnapshotBuildResult Create(
        RenderFrameSnapshot snapshot,
        ImmutableArray<SnapshotDiagnostic> diagnostics,
        IMediaForgeDiagnosticsSink? diagnosticsSink = null) =>
        new(snapshot, diagnostics, diagnosticsSink);

    public RenderFrameSnapshot? TakeSnapshot()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return null;

        return Interlocked.Exchange(ref _snapshot, null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var snapshot = Interlocked.Exchange(ref _snapshot, null);
        if (snapshot is null)
            return;

        try
        {
            snapshot.Dispose();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "render.snapshot_dispose_failed",
                "Failed to dispose render snapshot from build result.",
                nameof(SnapshotBuildResult),
                ex);
        }
    }
}
