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
            Assert.NotEqual(EncodedVideoBitstreamFormat.Unknown, packet.BitstreamFormat);
            if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Avcc)
            {
                Assert.False(packet.CodecConfiguration.IsEmpty);
            }

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

}
