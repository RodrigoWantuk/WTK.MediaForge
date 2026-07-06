using WTK.MediaForge.Composition.Runtime.Streaming;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Composition.Tests.Gpu;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Streaming;

public sealed class TextureLeaseQueueTests
{
    [Fact]
    public void Queue_keep_latest_drops_older_leases_and_disposes_them()
    {
        using var pool = new GpuResourcePool(new FakeTextureFactory());
        using var queue = new TextureLeaseQueue(capacity: 2, TextureLeaseQueuePolicy.KeepLatest);

        var descriptor = new GpuTextureDescriptor
        {
            Width = 64,
            Height = 64,
            Usage = GpuTextureUsage.OffscreenColor
        };

        var first = pool.AcquireTexture(descriptor);
        var second = pool.AcquireTexture(descriptor);
        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryAcquire(out var acquired));
        Assert.NotNull(acquired);
        Assert.Equal(second.TextureId, acquired!.TextureId);
    }

    [Fact]
    public void Consumer_receives_same_gpu_lease_without_copy()
    {
        using var pool = new GpuResourcePool(new FakeTextureFactory());
        using var queue = new TextureLeaseQueue(capacity: 1, TextureLeaseQueuePolicy.Queue);

        var lease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 32,
            Height = 32,
            Usage = GpuTextureUsage.OffscreenColor
        });

        queue.Enqueue(lease);
        Assert.True(queue.TryAcquire(out var acquired));
        Assert.Equal(lease.TextureId, acquired!.TextureId);
    }
}
