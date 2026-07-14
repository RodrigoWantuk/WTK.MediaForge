using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class HardwareEncoderInputLeaseTests
{
    [Fact]
    public void Retained_backend_surface_survives_original_lease_dispose_until_retention_dispose()
    {
        var releaseCount = 0;
        var backendSurface = new object();
        using var lease = HardwareEncoderInputLease.CreateWithBackendSurface(
            new GpuVideoFrameDescriptor
            {
                Width = 320,
                Height = 180,
                Format = "NV12",
                TransportKind = MediaTransportKind.GpuSurface
            },
            backendSurface,
            () => releaseCount++);

        var retention = lease.RetainBackendSurfaceForAsyncConsumer();

        lease.Dispose();
        Assert.Equal(0, releaseCount);
        Assert.Same(backendSurface, retention.BackendSurface);

        retention.Dispose();
        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void Backend_surface_retention_rejects_disposed_input_lease()
    {
        var lease = HardwareEncoderInputLease.CreateWithBackendSurface(
            new GpuVideoFrameDescriptor
            {
                Width = 320,
                Height = 180,
                Format = "NV12",
                TransportKind = MediaTransportKind.GpuSurface
            },
            new object());
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => lease.RetainBackendSurfaceForAsyncConsumer());
    }
}
