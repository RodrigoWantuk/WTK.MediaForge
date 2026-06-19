using System.Collections.Immutable;

namespace WTK.MediaForge.Composition.Snapshots;

public sealed class SnapshotBuildResult : IDisposable
{
    private int _disposed;
    private RenderFrameSnapshot? _snapshot;

    private SnapshotBuildResult(RenderFrameSnapshot snapshot, ImmutableArray<SnapshotDiagnostic> diagnostics)
    {
        _snapshot = snapshot;
        Diagnostics = diagnostics;
    }

    public RenderFrameSnapshot? Snapshot => _snapshot;

    public ImmutableArray<SnapshotDiagnostic> Diagnostics { get; }

    public static SnapshotBuildResult Create(
        RenderFrameSnapshot snapshot,
        ImmutableArray<SnapshotDiagnostic> diagnostics) =>
        new(snapshot, diagnostics);

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
        catch (Exception)
        {
            // TODO: Diagnostics.Record snapshot dispose failure.
        }
    }
}
