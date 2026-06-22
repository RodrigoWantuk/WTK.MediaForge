using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class SourceFrameBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly MediaSourceBufferOptions _options;
    private readonly Queue<RetainedFrame> _queue = [];
    private RetainedFrame? _current;
    private bool _disposed;

    public SourceFrameBuffer(MediaSourceBufferOptions? options = null) =>
        _options = options ?? new MediaSourceBufferOptions();

    public int Count
    {
        get
        {
            lock (_gate)
                return _options.Mode == MediaSourceBufferMode.Queue ? _queue.Count : (_current is null ? 0 : 1);
        }
    }

    public void Publish(GpuFrameLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        RetainedFrame? dropped = null;
        var accepted = false;

        try
        {
            var retained = new RetainedFrame(lease);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                switch (_options.Mode)
                {
                    case MediaSourceBufferMode.Queue:
                        _queue.Enqueue(retained);
                        accepted = true;
                        if (_queue.Count > _options.NormalizedCapacity)
                            dropped = _queue.Dequeue();
                        break;

                    case MediaSourceBufferMode.Static when _current is not null:
                        dropped = retained;
                        accepted = true;
                        break;

                    case MediaSourceBufferMode.KeepLatest:
                    case MediaSourceBufferMode.Static:
                    case MediaSourceBufferMode.TimelineDriven:
                        dropped = _current;
                        _current = retained;
                        accepted = true;
                        break;

                    default:
                        dropped = retained;
                        accepted = true;
                        throw new InvalidOperationException($"Unsupported source buffer mode '{_options.Mode}'.");
                }
            }
        }
        finally
        {
            if (!accepted)
                lease.Dispose();

            dropped?.ReleaseOwner();
        }
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _options.Mode == MediaSourceBufferMode.Queue
                ? TryAcquireQueuedFrameLocked(out lease)
                : TryAcquireCurrentFrameLocked(out lease);
        }
    }

    public bool TryTakeLatestFrame(out GpuFrameLease lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _options.Mode == MediaSourceBufferMode.Queue
                ? TryTakeQueuedFrameLocked(out lease)
                : TryTakeCurrentFrameLocked(out lease);
        }
    }

    public bool TryAcquireForRender(TimeSpan renderTimestamp, out GpuFrameLease lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _options.Mode switch
            {
                MediaSourceBufferMode.Queue => TryTakeQueuedFrameLocked(out lease),
                MediaSourceBufferMode.KeepLatest => TryAcquireCurrentFrameLocked(out lease),
                MediaSourceBufferMode.Static => TryAcquireCurrentFrameLocked(out lease),
                MediaSourceBufferMode.TimelineDriven => TryAcquireCurrentFrameLocked(out lease),
                _ => throw new InvalidOperationException($"Unsupported source buffer mode '{_options.Mode}'.")
            };
        }
    }

    public void Dispose()
    {
        List<RetainedFrame> retained = [];

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_current is not null)
            {
                retained.Add(_current);
                _current = null;
            }

            while (_queue.Count > 0)
                retained.Add(_queue.Dequeue());
        }

        foreach (var frame in retained)
            frame.ReleaseOwner();
    }

    private bool TryAcquireCurrentFrameLocked(out GpuFrameLease lease)
    {
        if (_current is null)
        {
            lease = null!;
            return false;
        }

        if (IsExpired(_current))
        {
            var expired = _current;
            _current = null;
            expired.ReleaseOwner();
            lease = null!;
            return false;
        }

        lease = _current.Retain();
        return true;
    }

    private bool TryAcquireQueuedFrameLocked(out GpuFrameLease lease)
    {
        while (_queue.Count > 0)
        {
            var retained = _queue.Dequeue();
            if (IsExpired(retained))
            {
                retained.ReleaseOwner();
                continue;
            }

            lease = retained.Retain();
            retained.ReleaseOwner();
            return true;
        }

        lease = null!;
        return false;
    }

    private bool TryTakeCurrentFrameLocked(out GpuFrameLease lease)
    {
        if (_current is null)
        {
            lease = null!;
            return false;
        }

        var retained = _current;
        _current = null;

        if (IsExpired(retained))
        {
            retained.ReleaseOwner();
            lease = null!;
            return false;
        }

        lease = retained.TransferToLease();
        return true;
    }

    private bool TryTakeQueuedFrameLocked(out GpuFrameLease lease)
    {
        while (_queue.Count > 0)
        {
            var retained = _queue.Dequeue();
            if (IsExpired(retained))
            {
                retained.ReleaseOwner();
                continue;
            }

            lease = retained.TransferToLease();
            return true;
        }

        lease = null!;
        return false;
    }

    private bool IsExpired(RetainedFrame frame)
    {
        if (_options.MaxFrameAge is not { } maxAge)
            return false;

        var elapsedTicks = Environment.TickCount64 - frame.PublishedTick;
        return elapsedTicks >= maxAge.TotalMilliseconds;
    }

    private sealed class RetainedFrame
    {
        private readonly object _gate = new();
        private readonly GpuFrameLease _ownerLease;
        private int _childRefCount;
        private bool _ownerReleased;

        public RetainedFrame(GpuFrameLease ownerLease)
        {
            _ownerLease = ownerLease;
            PublishedTick = Environment.TickCount64;
        }

        public long PublishedTick { get; }

        public GpuFrameLease Retain()
        {
            lock (_gate)
            {
                if (_ownerReleased)
                    throw new ObjectDisposedException(nameof(SourceFrameBuffer));

                _childRefCount++;
                return GpuFrameLease.Create(_ownerLease.Frame, ReleaseChild);
            }
        }

        public GpuFrameLease TransferToLease()
        {
            lock (_gate)
            {
                if (_ownerReleased)
                    throw new ObjectDisposedException(nameof(SourceFrameBuffer));

                _childRefCount++;
                _ownerReleased = true;
                return GpuFrameLease.Create(_ownerLease.Frame, ReleaseChild);
            }
        }

        public void ReleaseOwner()
        {
            var shouldDispose = false;

            lock (_gate)
            {
                if (_ownerReleased)
                    return;

                _ownerReleased = true;
                shouldDispose = _childRefCount == 0;
            }

            if (shouldDispose)
                _ownerLease.Dispose();
        }

        private void ReleaseChild()
        {
            var shouldDispose = false;

            lock (_gate)
            {
                _childRefCount--;
                if (_childRefCount < 0)
                    throw new InvalidOperationException("Source frame lease was released more times than it was acquired.");

                shouldDispose = _ownerReleased && _childRefCount == 0;
            }

            if (shouldDispose)
                _ownerLease.Dispose();
        }
    }
}
