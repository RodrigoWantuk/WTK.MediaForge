namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanSubmissionResourceMetrics
{
    private int _liveFramebuffers;
    private int _liveDescriptorSets;
    private int _framebufferHighWaterMark;
    private int _descriptorSetHighWaterMark;

    public void RetainFramebuffer()
    {
        var current = Interlocked.Increment(ref _liveFramebuffers);
        UpdateMaximum(ref _framebufferHighWaterMark, current);
    }

    public void ReleaseFramebuffer() => Interlocked.Decrement(ref _liveFramebuffers);

    public void RetainDescriptorSet()
    {
        var current = Interlocked.Increment(ref _liveDescriptorSets);
        UpdateMaximum(ref _descriptorSetHighWaterMark, current);
    }

    public void ReleaseDescriptorSet() => Interlocked.Decrement(ref _liveDescriptorSets);

    public VulkanSubmissionResourceMetricsSnapshot GetSnapshot() =>
        new(
            Volatile.Read(ref _liveFramebuffers),
            Volatile.Read(ref _liveDescriptorSets),
            Volatile.Read(ref _framebufferHighWaterMark),
            Volatile.Read(ref _descriptorSetHighWaterMark));

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}

internal readonly record struct VulkanSubmissionResourceMetricsSnapshot(
    int LiveFramebuffers,
    int LiveDescriptorSets,
    int FramebufferHighWaterMark,
    int DescriptorSetHighWaterMark);
