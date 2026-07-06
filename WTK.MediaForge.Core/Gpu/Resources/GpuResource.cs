namespace WTK.MediaForge.Core.Gpu.Resources;

internal abstract class GpuResource
{
    private int _leaseCount;
    private GpuResourceState _state = GpuResourceState.Active;

    protected GpuResource(GpuResourceKind kind)
    {
        Id = GpuResourceId.New();
        Kind = kind;
    }

    public GpuResourceId Id { get; }

    public GpuResourceKind Kind { get; }

    internal GpuResourceState State => _state;

    internal int LeaseCount => Volatile.Read(ref _leaseCount);

    internal void AddLeaseRef() => Interlocked.Increment(ref _leaseCount);

    internal bool ReleaseLeaseRef()
    {
        var remaining = Interlocked.Decrement(ref _leaseCount);
        if (remaining < 0)
        {
            Interlocked.Increment(ref _leaseCount);
            throw new InvalidOperationException("GPU resource lease released more times than acquired.");
        }

        return remaining == 0;
    }

    internal void MarkRetired() => _state = GpuResourceState.Retired;

    internal void MarkActive() => _state = GpuResourceState.Active;

    internal void MarkDisposed() => _state = GpuResourceState.Disposed;
}
