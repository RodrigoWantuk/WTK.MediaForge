namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuFence : GpuResource, IGpuPhysicalResource
{
    private readonly TaskCompletionSource _signaled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _fullyDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _finalized;

    public bool IsSignaled => _signaled.Task.IsCompletedSuccessfully;

    public Task FullyDisposed => _fullyDisposed.Task;

    public GpuFence()
        : base(GpuResourceKind.Fence)
    {
    }

    public void Signal()
    {
        _signaled.TrySetResult();
    }

    public bool TryFinalizePhysicalResources()
    {
        if (!IsSignaled)
            return false;

        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return _fullyDisposed.Task.IsCompleted;

        MarkDisposed();
        _fullyDisposed.TrySetResult();
        return true;
    }
}
