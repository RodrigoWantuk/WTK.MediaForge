using Silk.NET.Vulkan;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Resources;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanGpuResourcePool : IDisposable
{
    private readonly VulkanGpuTextureFactory _factory;
    private readonly GpuResourcePool _pool;
    private int _disposed;

    public VulkanGpuResourcePool(VulkanHeadlessDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _factory = new VulkanGpuTextureFactory(device);
        _pool = new GpuResourcePool(_factory);
    }

    internal GpuResourcePool Pool => _pool;

    internal int FactoryCreateCount => _factory.CreateCount;

    internal int ActiveTextureCount => _pool.ActiveTextureCount;

    internal int AvailableTextureCount => _pool.AvailableTextureCount;

    internal int PendingFenceTextureCount => _pool.PendingFenceTextureCount;

    internal int PendingRetiredResourceCount => _pool.RetiredResources.PendingCount;

    internal int FailedRetiredResourceCount => _pool.RetiredResources.FailedCount;

    internal int PhysicalHighWaterMark => _pool.PhysicalHighWaterMark;

    internal (GpuTextureLease Lease, VulkanOffscreenRenderTarget Target) AcquireOffscreenTarget(
        FrameSize size,
        GpuTextureUsage usage = GpuTextureUsage.OffscreenColor)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (size.Width == 0 || size.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                "Offscreen target dimensions must be greater than zero.");
        }

        var lease = _pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = (int)size.Width,
            Height = (int)size.Height,
            Usage = usage,
            Recyclable = true
        });

        var physical = (VulkanOffscreenPhysicalTexture)lease.Texture.Physical!;
        physical.Target.CurrentLayout = ImageLayout.Undefined;
        return (lease, physical.Target);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _pool.Dispose();
    }
}
