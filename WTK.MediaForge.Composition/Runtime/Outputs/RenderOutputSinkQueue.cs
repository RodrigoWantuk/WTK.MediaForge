using WTK.MediaForge.Composition.Outputs;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

internal sealed class RenderOutputSinkQueue
{
    private readonly object _gate = new();
    private readonly Queue<RenderOutputFrameLease> _queue = [];
    private readonly int _capacity;
    private readonly RenderOutputSinkBackpressureMode _backpressureMode;
    private readonly Action<RenderOutputFrameLease> _dropFrame;
    private bool _accepting = true;

    public RenderOutputSinkQueue(
        int capacity,
        RenderOutputSinkBackpressureMode backpressureMode,
        Action<RenderOutputFrameLease> dropFrame)
    {
        _capacity = Math.Max(1, capacity);
        _backpressureMode = backpressureMode;
        _dropFrame = dropFrame ?? throw new ArgumentNullException(nameof(dropFrame));
    }

    public bool IsAccepting
    {
        get
        {
            lock (_gate)
                return _accepting;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _queue.Count;
        }
    }

    public bool TryEnqueue(RenderOutputFrameLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        RenderOutputFrameLease? dropped = null;
        var enqueued = false;

        lock (_gate)
        {
            if (!_accepting)
            {
                dropped = lease;
            }
            else if (_queue.Count < _capacity)
            {
                _queue.Enqueue(lease);
                enqueued = true;
            }
            else
            {
                switch (_backpressureMode)
                {
                    case RenderOutputSinkBackpressureMode.DropNewest:
                        dropped = lease;
                        break;
                    case RenderOutputSinkBackpressureMode.DropOldest:
                    case RenderOutputSinkBackpressureMode.KeepLatest:
                        dropped = _queue.Dequeue();
                        _queue.Enqueue(lease);
                        enqueued = true;
                        break;
                    default:
                        dropped = lease;
                        break;
                }
            }
        }

        if (dropped is not null)
            _dropFrame(dropped);

        return enqueued;
    }

    public bool TryDequeue(out RenderOutputFrameLease lease)
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                lease = null!;
                return false;
            }

            lease = _queue.Dequeue();
            return true;
        }
    }

    public void StopAccepting()
    {
        lock (_gate)
            _accepting = false;
    }

    public IReadOnlyList<RenderOutputFrameLease> Drain()
    {
        List<RenderOutputFrameLease> leases = [];

        lock (_gate)
        {
            while (_queue.Count > 0)
                leases.Add(_queue.Dequeue());
        }

        return leases;
    }
}
