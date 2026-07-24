using Vortice.DXGI;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Windows.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[Trait("Category", "GPU")]
public sealed class WindowsGpuExportProofTests
{
    [Fact]
    public async Task Gpu_export_proof_creates_encoder_surface_without_cpu_readback()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var audit = new CollectingMediaTransportAuditSink();
        var exporter = new VulkanToD3D11EncoderSurfaceExporter(gpuDevice.Device);

        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = 640,
            Height = 360,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };

        var requirement = new HardwareEncoderInputRequirement
        {
            Width = 640,
            Height = 360,
            PixelFormat = "B8G8R8A8_UNORM",
            RequiresGpuSurface = true
        };

        Assert.True(exporter.CanExport(descriptor, requirement));

        using var lease = await exporter.ExportForEncoderAsync(descriptor, audit, CancellationToken.None);

        Assert.NotNull(lease);
        Assert.True(MediaTransportAuditRules.IsProductPathValid(audit.Events));
        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
        Assert.True(audit.Contains(MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated));
        Assert.Contains(audit.Events, e =>
            e.Kind == MediaTransportAuditEventKind.GpuSurfaceExportSucceeded &&
            e.EvidenceKind == MediaTransportAuditEvidenceKind.BackendCallSucceeded);
    }

    [Fact]
    public void Gpu_exporter_rejects_pixel_format_mismatch_without_gpu_converter()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        var exporter = new VulkanToD3D11EncoderSurfaceExporter(gpuDevice.Device);
        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = 640,
            Height = 360,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };
        var requirement = new HardwareEncoderInputRequirement
        {
            Width = 640,
            Height = 360,
            PixelFormat = "NV12",
            RequiresGpuSurface = true
        };

        Assert.False(exporter.CanExport(descriptor, requirement));
    }
}
