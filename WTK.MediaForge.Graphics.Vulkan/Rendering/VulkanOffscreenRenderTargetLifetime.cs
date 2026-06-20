namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanOffscreenRenderTargetLifetime
{
    public static int LiveCount;

    public static int DisposeCount;

    public static void Reset()
    {
        LiveCount = 0;
        DisposeCount = 0;
    }
}
