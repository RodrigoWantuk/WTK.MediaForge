using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Windows.Media.Encode;
using WTK.MediaForge.Windows.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

[Trait("Category", "GPU")]
public sealed class WindowsGpuExportEndToEndProofTests
{
    [Fact]
    public async Task Prototype_vulkan_export_mf_encode_does_not_satisfy_product_export_proof()
    {
        using var vulkanDevice = VulkanHeadlessDevice.Create();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        const int width = 640;
        const int height = 360;

        using var offscreen = new VulkanOffscreenRenderTarget(vulkanDevice, new FrameSize(width, height));
        VulkanD3D11ExportBlit.ClearOffscreenColor(offscreen, 0.2f, 0.4f, 0.9f, 1f);

        var audit = new CollectingMediaTransportAuditSink();
        var exporter = new VulkanToD3D11EncoderSurfaceExporter(vulkanDevice, gpuDevice.Device);

        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = width,
            Height = height,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };

        using var lease = await exporter.ExportForEncoderAsync(descriptor, offscreen, audit, CancellationToken.None);

        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width,
            height,
            pixelFormat: "B8G8R8A8_UNORM",
            allowPrototypeEncoding: true);
        var packet = await encoder.EncodeAsync(
            new EncodeFrameContext
            {
                InputLease = lease,
                FrameNumber = 1,
                PresentationTime = TimeSpan.Zero,
                CancellationToken = CancellationToken.None
            },
            audit);

        Assert.NotNull(packet);
        Assert.False(packet!.Data.IsEmpty);
        Assert.Equal(EncodedVideoCodec.H264, packet.Codec);
        Assert.True(ContainsH264StartCode(packet.Data.Span));
        Assert.True(ContainsValidNalType(packet.Data.Span));

        Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
        Assert.True(audit.Contains(MediaTransportAuditEventKind.GpuSurfaceExportSucceeded));
        Assert.True(audit.Contains(MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface));
        Assert.True(audit.Contains(MediaTransportAuditEventKind.EncodedPacketProduced));

        Assert.Contains(audit.Events, e =>
            e.Kind == MediaTransportAuditEventKind.HardwareEncoderAcceptedSurface &&
            e.EvidenceKind == MediaTransportAuditEvidenceKind.Prototype);
        Assert.Contains(audit.Events, e =>
            e.Kind == MediaTransportAuditEventKind.EncodedPacketProduced &&
            e.EvidenceKind == MediaTransportAuditEvidenceKind.Prototype);
    }

    [Fact]
    public async Task Real_vulkan_export_mf_encode_satisfies_product_export_proof_or_reports_unavailable()
    {
        using var vulkanDevice = VulkanHeadlessDevice.Create();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter).CheckError();
        using var gpuDevice = D3D11GpuDevice.CreateForAdapter(adapter);

        const int width = 640;
        const int height = 360;

        using var offscreen = new VulkanOffscreenRenderTarget(vulkanDevice, new FrameSize(width, height));
        VulkanD3D11ExportBlit.ClearOffscreenColor(offscreen, 0.2f, 0.4f, 0.9f, 1f);

        var audit = new CollectingMediaTransportAuditSink();
        var exporter = new VulkanToD3D11EncoderSurfaceExporter(vulkanDevice, gpuDevice.Device);

        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = width,
            Height = height,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        };

        using var lease = await exporter.ExportForEncoderAsync(descriptor, offscreen, audit, CancellationToken.None);
        await using var encoder = new MediaFoundationHardwareVideoEncoder(
            gpuDevice.Device,
            width,
            height,
            pixelFormat: "B8G8R8A8_UNORM");

        try
        {
            var packet = await encoder.EncodeAsync(
                new EncodeFrameContext
                {
                    InputLease = lease,
                    FrameNumber = 1,
                    PresentationTime = TimeSpan.Zero,
                    CancellationToken = CancellationToken.None
                },
                audit);

            if (packet is null)
            {
                Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
                return;
            }

            Assert.False(packet.Data.IsEmpty);
            Assert.Equal(EncodedVideoCodec.H264, packet.Codec);
            Assert.True(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
        }
        catch (NotSupportedException ex)
        {
            Assert.Contains("hardware", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(MediaTransportAuditRules.IsExportProofPathValid(audit.Events));
            Assert.DoesNotContain(audit.Events, e => e.EvidenceKind == MediaTransportAuditEvidenceKind.Prototype);
        }

        Assert.False(audit.Contains(MediaTransportAuditEventKind.CpuReadbackAttempted));
        Assert.False(audit.Contains(MediaTransportAuditEventKind.StagingBufferCreated));
    }

    private static bool ContainsH264StartCode(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 || (data[i + 2] == 0 && data[i + 3] == 1)))
                return true;
        }

        return false;
    }

    private static bool ContainsValidNalType(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 4 < data.Length; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && (data[i + 2] == 1 || (data[i + 2] == 0 && data[i + 3] == 1)))
            {
                var nalOffset = data[i + 2] == 1 ? i + 3 : i + 4;
                if (nalOffset >= data.Length)
                    continue;

                var nalType = data[nalOffset] & 0x1F;
                return nalType is >= 1 and <= 12;
            }
        }

        return false;
    }
}
