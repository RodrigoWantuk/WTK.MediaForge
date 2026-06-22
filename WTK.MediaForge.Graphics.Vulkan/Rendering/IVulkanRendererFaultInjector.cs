namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal interface IVulkanRendererFaultInjector
{
    void BeforeAcquireTexture(int attempt);

    void BeforeQueueSubmit();

    void AfterFailedSubmitCleanup(
        bool commandBufferFreed,
        bool fenceDestroyed,
        int textureLeaseDisposeCount);
}

internal sealed class NullVulkanRendererFaultInjector : IVulkanRendererFaultInjector
{
    public static NullVulkanRendererFaultInjector Instance { get; } = new();

    public void BeforeAcquireTexture(int attempt) { }

    public void BeforeQueueSubmit() { }

    public void AfterFailedSubmitCleanup(
        bool commandBufferFreed,
        bool fenceDestroyed,
        int textureLeaseDisposeCount)
    {
    }
}
