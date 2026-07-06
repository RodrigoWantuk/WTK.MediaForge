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

    public ValueTask<GpuTextureLease?> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_gate)
        {
            if (_queue.Count == 0)
                return ValueTask.FromResult<GpuTextureLease?>(null);

            return ValueTask.FromResult<GpuTextureLease?>(_queue.Dequeue());
        }
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

        Clear();
    }
}
