using WTK.MediaForge.Core.Gpu.Resources;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Gpu;

public sealed class GpuResourcePoolTests
{
    [Fact]
    public void Acquire_and_release_recycles_texture_when_dimensions_match()
    {
        var factory = new FakeTextureFactory();
        using var pool = new GpuResourcePool(factory);

        var descriptor = new GpuTextureDescriptor
        {
            Width = 640,
            Height = 360,
            Usage = GpuTextureUsage.OffscreenColor
        };

        using (pool.AcquireTexture(descriptor))
        {
            Assert.Equal(1, factory.CreateCount);
            Assert.Equal(1, pool.ActiveTextureCount);
            Assert.Equal(1, pool.PhysicalHighWaterMark);
        }

        using (pool.AcquireTexture(descriptor))
        {
            Assert.Equal(1, factory.CreateCount);
        }

        Assert.Equal(1, pool.AvailableTextureCount);
        Assert.Equal(1, pool.PhysicalHighWaterMark);
    }

    [Fact]
    public void Retired_texture_not_reused_until_fence_completes()
    {
        var factory = new FakeTextureFactory();
        using var pool = new GpuResourcePool(factory);

        var descriptor = new GpuTextureDescriptor
        {
            Width = 1280,
            Height = 720,
            Usage = GpuTextureUsage.Intermediate
        };

        GpuTexture texture;
        using (var lease = pool.AcquireTexture(descriptor))
        {
            texture = lease.Texture;
            texture.RetirementFence = new GpuFence();
            Assert.Equal(1, factory.CreateCount);
        }

        using (var secondLease = pool.AcquireTexture(descriptor))
        {
            Assert.Equal(2, factory.CreateCount);
            Assert.NotSame(texture, secondLease.Texture);
            Assert.Equal(1, pool.PendingFenceTextureCount);
            Assert.Equal(2, pool.PhysicalHighWaterMark);
        }

        texture.RetirementFence!.Signal();
        pool.CollectRetired();

        using (var thirdLease = pool.AcquireTexture(descriptor))
        {
            Assert.Equal(2, factory.CreateCount);
            Assert.Same(texture, thirdLease.Texture);
        }
    }

    [Fact]
    public async Task Dispose_failure_surfaces_as_fault_not_success()
    {
        var factory = new FakeTextureFactory(faultOnFinalize: true);
        var pool = new GpuResourcePool(factory);

        var descriptor = new GpuTextureDescriptor
        {
            Width = 320,
            Height = 240,
            Recyclable = false,
            Usage = GpuTextureUsage.EncoderInput
        };

        using (pool.AcquireTexture(descriptor))
        {
            Assert.Equal(1, factory.CreateCount);
        }

        pool.CollectRetired();
        Assert.Equal(1, pool.RetiredResources.FailedCount);

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            pool.WaitForRetiredAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask());

        Assert.Contains(ex.InnerExceptions, inner => inner is InvalidOperationException);
    }
}
