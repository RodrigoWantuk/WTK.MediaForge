using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

internal sealed class TestVulkanRendererFaultInjector : IVulkanRendererFaultInjector
{
    public int? FailAcquireOnAttempt { get; set; }

    public bool FailQueueSubmit { get; set; }

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
}
