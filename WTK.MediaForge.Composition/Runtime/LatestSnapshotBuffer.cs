using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime;

public sealed class LatestSnapshotBuffer : IDisposable
{
    private readonly object _gate = new();
    private RenderFrameSnapshot? _latest;
    private bool _disposed;

    public void Publish(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        RenderFrameSnapshot? previous;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _latest;
            _latest = snapshot;
        }

        previous?.Dispose();
    }

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _latest is not null;
            }
        }
    }

    public RenderFrameSnapshot? AcquireLatest()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var acquired = _latest;
            _latest = null;
            return acquired;
        }
    }

    public void Dispose()
    {
        RenderFrameSnapshot? remaining;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            remaining = _latest;
            _latest = null;
        }

        remaining?.Dispose();
    }
}
