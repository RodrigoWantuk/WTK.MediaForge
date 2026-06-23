using WTK.MediaForge.Composition.Outputs;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

internal sealed class RenderOutputSinkQueue
{
    private readonly object _gate = new();
    private readonly Queue<RenderOutputFrameLease> _queue = [];
    private readonly int _capacity;
    private readonly RenderOutputSinkBackpressureMode _backpressureMode;
    private bool _accepting = true;

    public RenderOutputSinkQueue(
        int capacity,
        RenderOutputSinkBackpressureMode backpressureMode)
    {
        _capacity = Math.Max(1, capacity);
        _backpressureMode = backpressureMode;
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

    public RenderOutputSinkQueueEnqueueResult TryEnqueue(
        RenderOutputFrameLease lease,
        out RenderOutputFrameLease? leaseForCallerToRelease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        leaseForCallerToRelease = null;

        lock (_gate)
        {
            if (!_accepting)
            {
                leaseForCallerToRelease = lease;
                return RenderOutputSinkQueueEnqueueResult.RejectedCallerMustRelease;
            }

            if (_queue.Count < _capacity)
            {
                _queue.Enqueue(lease);
                return RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled;
            }

            switch (_backpressureMode)
            {
                case RenderOutputSinkBackpressureMode.DropNewest:
                    leaseForCallerToRelease = lease;
                    return RenderOutputSinkQueueEnqueueResult.DroppedIncomingReturnedToCaller;
                case RenderOutputSinkBackpressureMode.DropOldest:
                case RenderOutputSinkBackpressureMode.KeepLatest:
                    leaseForCallerToRelease = _queue.Dequeue();
                    _queue.Enqueue(lease);
                    return RenderOutputSinkQueueEnqueueResult.ReplacedPendingOldReturnedToCaller;
                default:
                    leaseForCallerToRelease = lease;
                    return RenderOutputSinkQueueEnqueueResult.DroppedIncomingReturnedToCaller;
            }
        }
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

internal enum RenderOutputSinkQueueEnqueueResult
{
    EnqueuedAndWorkerSignaled = 0,
    ReplacedPendingOldReturnedToCaller = 1,
    DroppedIncomingReturnedToCaller = 2,
    RejectedCallerMustRelease = 3
}
