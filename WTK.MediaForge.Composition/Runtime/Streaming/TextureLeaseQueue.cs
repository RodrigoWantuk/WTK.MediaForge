using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Composition.Runtime.Streaming;

public enum TextureLeaseQueuePolicy
{
    Queue,
    KeepLatest
}

public interface ITextureStreamConsumer
{
    bool TryAcquire(out GpuTextureLease? lease);

    ValueTask<GpuTextureLease?> AcquireAsync(CancellationToken cancellationToken = default);
}

public sealed class TextureLeaseQueue : ITextureStreamConsumer, IDisposable
{
    private readonly int _capacity;
    private readonly TextureLeaseQueuePolicy _policy;
    private readonly Queue<GpuTextureLease> _queue = new();
    private readonly Queue<TaskCompletionSource<GpuTextureLease?>> _waiters = new();
    private readonly object _gate = new();
    private int _disposed;

    public TextureLeaseQueue(int capacity, TextureLeaseQueuePolicy policy = TextureLeaseQueuePolicy.KeepLatest)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _policy = policy;
    }

    public int Count
    {
        get
        {
            lock (_gate)
                return _queue.Count;
        }
    }

    public void Enqueue(GpuTextureLease lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(lease);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (TryDequeueWaiter(out var waiter))
            {
                if (!waiter!.TrySetResult(lease))
                    lease.Dispose();

                return;
            }

            if (_policy == TextureLeaseQueuePolicy.KeepLatest)
            {
                while (_queue.Count > 0)
                    _queue.Dequeue().Dispose();

                _queue.Enqueue(lease);
                return;
            }

            while (_queue.Count >= _capacity)
                _queue.Dequeue().Dispose();

            _queue.Enqueue(lease);
        }
    }

    public bool TryAcquire(out GpuTextureLease? lease)
    {
        lease = null;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_gate)
        {
            if (_queue.Count == 0)
                return false;

            lease = _queue.Dequeue();
            return true;
        }
    }

    public async ValueTask<GpuTextureLease?> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        TaskCompletionSource<GpuTextureLease?> waiter;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_queue.Count > 0)
                return _queue.Dequeue();

            waiter = new TaskCompletionSource<GpuTextureLease?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
        }

        using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var (queue, pendingWaiter) =
                ((TextureLeaseQueue Queue, TaskCompletionSource<GpuTextureLease?> Waiter))state!;
            queue.CancelWaiter(pendingWaiter);
        }, (this, waiter));

        return await waiter.Task.ConfigureAwait(false);
    }

    public void Clear()
    {
        lock (_gate)
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        TaskCompletionSource<GpuTextureLease?>[] waiters;

        lock (_gate)
        {
            while (_queue.Count > 0)
                _queue.Dequeue().Dispose();

            waiters = _waiters.ToArray();
            _waiters.Clear();
        }

        foreach (var waiter in waiters)
            waiter.TrySetException(new ObjectDisposedException(nameof(TextureLeaseQueue)));
    }

    private bool TryDequeueWaiter(out TaskCompletionSource<GpuTextureLease?>? waiter)
    {
        while (_waiters.Count > 0)
        {
            var candidate = _waiters.Dequeue();
            if (candidate.Task.IsCompleted)
                continue;

            waiter = candidate;
            return true;
        }

        waiter = null;
        return false;
    }

    private void CancelWaiter(TaskCompletionSource<GpuTextureLease?> waiter)
    {
        lock (_gate)
        {
            if (waiter.Task.IsCompleted)
                return;

            var remaining = _waiters
                .Where(candidate => !ReferenceEquals(candidate, waiter))
                .ToArray();
            _waiters.Clear();
            foreach (var candidate in remaining)
                _waiters.Enqueue(candidate);

            waiter.TrySetCanceled();
        }
    }
}
