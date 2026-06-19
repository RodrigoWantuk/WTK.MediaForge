using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime;

public sealed class LatestSnapshotBuffer : IDisposable
{
    private RenderFrameSnapshot? _latest;
    private int _disposed;

    public void Publish(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var previous = Interlocked.Exchange(ref _latest, snapshot);
        previous?.Dispose();
    }

    public RenderFrameSnapshot? AcquireLatest()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Interlocked.Exchange(ref _latest, null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var remaining = Interlocked.Exchange(ref _latest, null);
        remaining?.Dispose();
    }
}
