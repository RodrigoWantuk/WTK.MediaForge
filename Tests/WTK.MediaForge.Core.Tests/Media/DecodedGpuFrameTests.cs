using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Decode;
using WTK.MediaForge.Core.Tests.Gpu;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class DecodedGpuFrameTests
{
    [Fact]
    public void Dispose_releases_texture_lease()
    {
        var pool = new GpuResourcePool(new FakeTextureFactory());
        var descriptor = new GpuTextureDescriptor
        {
            Width = 64,
            Height = 64,
            Usage = GpuTextureUsage.OffscreenColor
        };
        var lease = pool.AcquireTexture(descriptor);

        var frame = new DecodedGpuFrame(lease, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(33));
        Assert.Equal(64, frame.Width);
        Assert.Equal(64, frame.Height);

        frame.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = frame.TextureLease);
    }

    [Fact]
    public void Presentation_time_and_duration_are_preserved()
    {
        var pool = new GpuResourcePool(new FakeTextureFactory());
        var descriptor = new GpuTextureDescriptor
        {
            Width = 16,
            Height = 16,
            Usage = GpuTextureUsage.OffscreenColor
        };
        var lease = pool.AcquireTexture(descriptor);

        using var frame = new DecodedGpuFrame(
            lease,
            presentationTime: TimeSpan.FromSeconds(2),
            duration: TimeSpan.FromMilliseconds(40));

        Assert.Equal(TimeSpan.FromSeconds(2), frame.PresentationTime);
        Assert.Equal(TimeSpan.FromMilliseconds(40), frame.Duration);
    }

    [Fact]
    public void TakeTextureLease_transfers_ownership_to_caller()
    {
        var pool = new GpuResourcePool(new FakeTextureFactory());
        var descriptor = new GpuTextureDescriptor
        {
            Width = 16,
            Height = 16,
            Usage = GpuTextureUsage.OffscreenColor
        };
        var lease = pool.AcquireTexture(descriptor);
        var leaseId = lease.TextureId;

        var frame = new DecodedGpuFrame(
            lease,
            presentationTime: TimeSpan.Zero,
            duration: TimeSpan.FromMilliseconds(33));

        var transferred = frame.TakeTextureLease();

        Assert.Equal(leaseId, transferred.TextureId);
        Assert.Throws<ObjectDisposedException>(() => _ = frame.TextureLease);

        frame.Dispose();
        Assert.Equal(leaseId, transferred.TextureId);

        transferred.Dispose();
    }
}
