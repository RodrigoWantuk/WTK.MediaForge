using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

internal sealed class TestVulkanRendererFaultInjector : IVulkanRendererFaultInjector
{
    public int? FailAcquireOnAttempt { get; set; }

    public bool FailQueueSubmit { get; set; }

    public int FailedSubmitCleanupCount => Volatile.Read(ref _failedSubmitCleanupCount);

    public int FreedCommandBufferCount => Volatile.Read(ref _freedCommandBufferCount);

    public int DestroyedFenceCount => Volatile.Read(ref _destroyedFenceCount);

    public int DisposedTextureLeaseCount => Volatile.Read(ref _disposedTextureLeaseCount);

    private int _failedSubmitCleanupCount;
    private int _freedCommandBufferCount;
    private int _destroyedFenceCount;
    private int _disposedTextureLeaseCount;

    public void BeforeAcquireTexture(int attempt)
    {
        if (FailAcquireOnAttempt == attempt)
            throw new InvalidOperationException("Simulated acquire failure.");
    }

    public void BeforeQueueSubmit()
    {
        if (FailQueueSubmit)
            throw new InvalidOperationException("vkQueueSubmit failed.");
    }

    public void AfterFailedSubmitCleanup(
        bool commandBufferFreed,
        bool fenceDestroyed,
        int textureLeaseDisposeCount)
    {
        Interlocked.Increment(ref _failedSubmitCleanupCount);

        if (commandBufferFreed)
            Interlocked.Increment(ref _freedCommandBufferCount);

        if (fenceDestroyed)
            Interlocked.Increment(ref _destroyedFenceCount);

        Interlocked.Add(ref _disposedTextureLeaseCount, textureLeaseDisposeCount);
    }
}
