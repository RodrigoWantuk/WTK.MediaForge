namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanSubmissionResourceLifetime
{
    public static int LiveFramebuffers;
    public static int DestroyedFramebuffers;
    public static int LiveDescriptorSets;
    public static int FreedDescriptorSets;

    public static void Reset()
    {
        Volatile.Write(ref LiveFramebuffers, 0);
        Volatile.Write(ref DestroyedFramebuffers, 0);
        Volatile.Write(ref LiveDescriptorSets, 0);
        Volatile.Write(ref FreedDescriptorSets, 0);
    }
}
