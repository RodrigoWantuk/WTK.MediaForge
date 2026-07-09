using WTK.MediaForge.Composition.Runtime.Streaming;
using WTK.MediaForge.Core.Gpu;
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
        Assert.Equal(default(GpuTextureId), first.TextureId);
        Assert.True(queue.TryAcquire(out var acquired));
        Assert.NotNull(acquired);
        Assert.Equal(second.TextureId, acquired!.TextureId);

        acquired.Dispose();
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

        acquired.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_waits_until_frame_is_enqueued()
    {
        using var pool = new GpuResourcePool(new FakeTextureFactory());
        using var queue = new TextureLeaseQueue(capacity: 1, TextureLeaseQueuePolicy.Queue);

        var pending = queue.AcquireAsync(CancellationToken.None).AsTask();
        await Task.Delay(25);
        Assert.False(pending.IsCompleted);

        var lease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 32,
            Height = 32,
            Usage = GpuTextureUsage.OffscreenColor
        });

        var expectedId = lease.TextureId;
        queue.Enqueue(lease);

        var acquired = await pending;

        Assert.NotNull(acquired);
        Assert.Equal(expectedId, acquired!.TextureId);
        acquired.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_cancellation_removes_waiter_without_consuming_later_frame()
    {
        using var pool = new GpuResourcePool(new FakeTextureFactory());
        using var queue = new TextureLeaseQueue(capacity: 1, TextureLeaseQueuePolicy.Queue);
        using var cancellation = new CancellationTokenSource();

        var pending = queue.AcquireAsync(cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        var lease = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 16,
            Height = 16,
            Usage = GpuTextureUsage.OffscreenColor
        });
        var expectedId = lease.TextureId;

        queue.Enqueue(lease);

        Assert.True(queue.TryAcquire(out var acquired));
        Assert.NotNull(acquired);
        Assert.Equal(expectedId, acquired!.TextureId);
        acquired.Dispose();
    }

    [Fact]
    public async Task Dispose_faults_pending_acquire_without_leaking()
    {
        var queue = new TextureLeaseQueue(capacity: 1, TextureLeaseQueuePolicy.Queue);
        var pending = queue.AcquireAsync(CancellationToken.None).AsTask();

        queue.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
    }
}
