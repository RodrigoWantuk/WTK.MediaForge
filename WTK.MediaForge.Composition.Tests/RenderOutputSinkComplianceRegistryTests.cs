using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputSinkComplianceRegistryTests
{
    [Fact]
    public void Product_sinks_never_accept_debug_cpu_readback_transport()
    {
        foreach (var entry in RenderOutputSinkComplianceRegistry.All.Where(e => e.IsProductSink))
        {
            Assert.NotEqual(MediaTransportKind.DebugOnlyCpuReadback, entry.Transport);
            Assert.False(RenderOutputSinkComplianceRegistry.AcceptsRawCpuFrames(entry.Kind));
        }
    }

    [Fact]
    public void CpuReadbackSink_is_debug_only_and_not_product()
    {
        var entry = Assert.Single(
            RenderOutputSinkComplianceRegistry.All,
            e => e.SinkTypeName == nameof(CpuReadbackSink));

        Assert.False(entry.IsProductSink);
        Assert.Equal(MediaTransportKind.DebugOnlyCpuReadback, entry.Transport);
        Assert.True(RenderOutputSinkComplianceRegistry.AcceptsRawCpuFrames(entry.Kind));
    }

    [Fact]
    public void Recording_and_rtmp_sinks_use_encoded_packet_transport()
    {
        var mp4 = Assert.Single(RenderOutputSinkComplianceRegistry.All, e => e.SinkTypeName == nameof(RecordingMp4PacketSink));
        var rtmp = Assert.Single(RenderOutputSinkComplianceRegistry.All, e => e.SinkTypeName == nameof(RtmpPacketSink));

        Assert.Equal(MediaTransportKind.EncodedPacket, mp4.Transport);
        Assert.Equal(MediaTransportKind.EncodedPacket, rtmp.Transport);
        Assert.True(mp4.IsProductSink);
        Assert.True(rtmp.IsProductSink);
        Assert.Equal(MediaForgeSupportStatus.Supported, mp4.SupportStatus);
        Assert.Equal(MediaForgeSupportStatus.Supported, rtmp.SupportStatus);
        Assert.Null(mp4.UnavailableReason);
        Assert.Null(rtmp.UnavailableReason);
    }

    [Fact]
    public void PreviewPanelSink_is_compliant_product_sink()
    {
        Assert.True(RenderOutputSinkComplianceRegistry.IsCompliantProductSink(typeof(PreviewPanelSink)));
    }

    [Fact]
    public void Srt_and_ndi_are_not_product_sinks()
    {
        var srt = Assert.Single(RenderOutputSinkComplianceRegistry.All, e => e.SinkTypeName == "SrtSink");
        var ndi = Assert.Single(RenderOutputSinkComplianceRegistry.All, e => e.SinkTypeName == "NdiSink");

        Assert.False(srt.IsProductSink);
        Assert.False(ndi.IsProductSink);
        Assert.Equal(MediaForgeSupportStatus.Unsupported, ndi.SupportStatus);
        Assert.False(string.IsNullOrWhiteSpace(srt.UnavailableReason));
        Assert.False(string.IsNullOrWhiteSpace(ndi.UnavailableReason));
    }

    [Fact]
    public void Non_product_or_unavailable_sinks_have_user_visible_reasons()
    {
        foreach (var entry in RenderOutputSinkComplianceRegistry.All)
        {
            if (entry.IsProductSink &&
                entry.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental)
            {
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(entry.UnavailableReason));
        }
    }
}
