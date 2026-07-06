namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuSemaphore : GpuResource, IGpuPhysicalResource
{
    private readonly TaskCompletionSource _fullyDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _finalized;

    public Task FullyDisposed => _fullyDisposed.Task;

    public GpuSemaphore()
        : base(GpuResourceKind.Semaphore)
    {
    }

    public bool TryFinalizePhysicalResources()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return _fullyDisposed.Task.IsCompleted;

        MarkDisposed();
        _fullyDisposed.TrySetResult();
        return true;
    }
}
