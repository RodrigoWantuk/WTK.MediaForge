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

    public SinkQueueEnqueueResult TryEnqueue(RenderOutputFrameLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (_gate)
        {
            if (!_accepting)
            {
                return new SinkQueueEnqueueResult(
                    SinkQueueEnqueueResultKind.Rejected,
                    lease);
            }

            if (_queue.Count == 0)
            {
                _queue.Enqueue(lease);
                return new SinkQueueEnqueueResult(SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue);
            }

            if (_queue.Count < _capacity)
            {
                _queue.Enqueue(lease);
                return new SinkQueueEnqueueResult(SinkQueueEnqueueResultKind.EnqueuedIntoNonEmptyQueue);
            }

            return _backpressureMode switch
            {
                RenderOutputSinkBackpressureMode.DropNewest => new SinkQueueEnqueueResult(
                    SinkQueueEnqueueResultKind.DroppedIncoming,
                    lease),
                RenderOutputSinkBackpressureMode.DropOldest or RenderOutputSinkBackpressureMode.KeepLatest =>
                    DequeueAndEnqueueReplacement(lease),
                _ => new SinkQueueEnqueueResult(
                    SinkQueueEnqueueResultKind.DroppedIncoming,
                    lease)
            };
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

    private SinkQueueEnqueueResult DequeueAndEnqueueReplacement(RenderOutputFrameLease lease)
    {
        var replaced = _queue.Dequeue();
        _queue.Enqueue(lease);
        return new SinkQueueEnqueueResult(SinkQueueEnqueueResultKind.ReplacedOldest, replaced);
    }
}

internal enum SinkQueueEnqueueResultKind
{
    EnqueuedIntoPreviouslyEmptyQueue,
    EnqueuedIntoNonEmptyQueue,
    ReplacedOldest,
    DroppedIncoming,
    Rejected
}

internal readonly record struct SinkQueueEnqueueResult(
    SinkQueueEnqueueResultKind Kind,
    RenderOutputFrameLease? LeaseToRelease = null)
{
    public bool ShouldSignalWorker =>
        Kind is SinkQueueEnqueueResultKind.EnqueuedIntoPreviouslyEmptyQueue;
}
