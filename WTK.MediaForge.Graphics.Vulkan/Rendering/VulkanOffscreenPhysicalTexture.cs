using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanOffscreenPhysicalTexture : IGpuPhysicalResource
{
    private readonly VulkanOffscreenRenderTarget _target;
    private readonly TaskCompletionSource _fullyDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _finalized;

    public VulkanOffscreenPhysicalTexture(VulkanOffscreenRenderTarget target) =>
        _target = target ?? throw new ArgumentNullException(nameof(target));

    public VulkanOffscreenRenderTarget Target => _target;

    public Task FullyDisposed => _fullyDisposed.Task;

    public bool TryFinalizePhysicalResources()
    {
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return _fullyDisposed.Task.IsCompletedSuccessfully;

        try
        {
            _target.Dispose();
            _fullyDisposed.TrySetResult();
            return true;
        }
        catch (Exception ex)
        {
            _fullyDisposed.TrySetException(ex);
            throw;
        }
    }
}
