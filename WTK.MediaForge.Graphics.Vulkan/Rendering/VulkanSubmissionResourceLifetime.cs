namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class VulkanSubmissionResourceLifetime
{
    public static int LiveFramebuffers;
    public static int DestroyedFramebuffers;
    public static int LiveDescriptorSets;
    public static int FreedDescriptorSets;
    public static int TextureLeaseDisposeCount;
    public static int LastDescriptorSetFreeOrder;
    public static int FirstTextureLeaseDisposeOrder;
    private static int _disposeOrder;

    public static void Reset()
    {
        Volatile.Write(ref LiveFramebuffers, 0);
        Volatile.Write(ref DestroyedFramebuffers, 0);
        Volatile.Write(ref LiveDescriptorSets, 0);
        Volatile.Write(ref FreedDescriptorSets, 0);
        Volatile.Write(ref TextureLeaseDisposeCount, 0);
        Volatile.Write(ref LastDescriptorSetFreeOrder, 0);
        Volatile.Write(ref FirstTextureLeaseDisposeOrder, 0);
        Volatile.Write(ref _disposeOrder, 0);
    }

    public static void RecordDescriptorSetFreed()
    {
        var order = Interlocked.Increment(ref _disposeOrder);
        Interlocked.Increment(ref FreedDescriptorSets);
        Volatile.Write(ref LastDescriptorSetFreeOrder, order);
    }

    public static void RecordTextureLeaseDisposeStarted()
    {
        var order = Interlocked.Increment(ref _disposeOrder);
        Interlocked.Increment(ref TextureLeaseDisposeCount);
        Interlocked.CompareExchange(ref FirstTextureLeaseDisposeOrder, order, 0);
    }
}
