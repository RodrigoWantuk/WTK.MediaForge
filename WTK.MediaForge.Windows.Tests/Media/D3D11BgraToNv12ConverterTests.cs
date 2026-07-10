using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Windows.Media.Encode;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class D3D11BgraToNv12ConverterTests
{
    [Fact]
    public async Task Bgra_to_nv12_converter_reports_unavailable_without_cpu_fallback()
    {
        var converter = new D3D11BgraToNv12Converter();
        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = 1920,
            Height = 1080,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };
        var requirement = new HardwareEncoderInputRequirement
        {
            Width = 1920,
            Height = 1080,
            PixelFormat = "NV12",
            RequiresGpuSurface = true
        };

        Assert.False(converter.CanConvert(descriptor, requirement));

        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        using var sourceTexture = pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 1920,
            Height = 1080,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });
        var audit = new CollectingMediaTransportAuditSink();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await converter.ConvertAsync(sourceTexture, requirement, audit));

        Assert.Contains("GPU conversion pass", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionUnavailable));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.GpuFormatConversionSucceeded));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
    }

    private sealed class FakeGpuTextureFactory : IGpuTextureFactory
    {
        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor)
        {
            _ = descriptor;
            return new FakePhysicalResource();
        }
    }

    private sealed class FakePhysicalResource : IGpuPhysicalResource
    {
        public Task FullyDisposed => Task.CompletedTask;

        public bool TryFinalizePhysicalResources() => true;
    }
}
