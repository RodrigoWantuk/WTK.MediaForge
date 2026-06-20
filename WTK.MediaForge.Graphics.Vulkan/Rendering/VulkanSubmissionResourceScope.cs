using Silk.NET.Vulkan;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed unsafe class VulkanSubmissionResourceScope : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly DescriptorPool _descriptorPool;
    private readonly List<Framebuffer> _framebuffers = [];
    private readonly List<DescriptorSet> _descriptorSets = [];
    private readonly List<VulkanOffscreenTargetHandle> _offscreenTargets = [];
    private int _disposed;

    public VulkanSubmissionResourceScope(
        Vk vk,
        Device device,
        DescriptorPool descriptorPool)
    {
        _vk = vk;
        _device = device;
        _descriptorPool = descriptorPool;
    }

    public void RetainFramebuffer(Framebuffer framebuffer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (framebuffer.Handle == 0)
            return;

        _framebuffers.Add(framebuffer);
        Interlocked.Increment(ref VulkanSubmissionResourceLifetime.LiveFramebuffers);
    }

    public void RetainDescriptorSet(DescriptorSet descriptorSet)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (descriptorSet.Handle == 0)
            return;

        _descriptorSets.Add(descriptorSet);
        Interlocked.Increment(ref VulkanSubmissionResourceLifetime.LiveDescriptorSets);
    }

    public void RetainOffscreenTarget(VulkanOffscreenTargetHandle target)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(target);

        target.RetainForSubmission();
        _offscreenTargets.Add(target);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        List<Exception>? errors = null;

        foreach (var framebuffer in _framebuffers)
        {
            try
            {
                _vk.DestroyFramebuffer(_device, framebuffer, null);
                Interlocked.Decrement(ref VulkanSubmissionResourceLifetime.LiveFramebuffers);
                Interlocked.Increment(ref VulkanSubmissionResourceLifetime.DestroyedFramebuffers);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        foreach (var descriptorSet in _descriptorSets)
        {
            try
            {
                var local = descriptorSet;
                var result = _vk.FreeDescriptorSets(_device, _descriptorPool, 1, &local);
                if (result != Result.Success)
                    throw new InvalidOperationException($"vkFreeDescriptorSets failed: {result}");

                Interlocked.Decrement(ref VulkanSubmissionResourceLifetime.LiveDescriptorSets);
                VulkanSubmissionResourceLifetime.RecordDescriptorSetFreed();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        foreach (var offscreenTarget in _offscreenTargets)
        {
            try
            {
                offscreenTarget.ReleaseSubmissionReference();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        _framebuffers.Clear();
        _descriptorSets.Clear();
        _offscreenTargets.Clear();

        if (errors is not null)
            throw new AggregateException("Failed to dispose Vulkan submission resources cleanly.", errors);
    }
}
