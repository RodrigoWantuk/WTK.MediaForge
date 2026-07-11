using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media;
using WTK.MediaForge.Windows.Media.Decode;
using WTK.MediaForge.Windows.Media.Encode;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsMediaCapabilityTruthTests
{
    [Fact]
    public async Task Windows_capability_probe_does_not_advertise_unvalidated_media_codecs()
    {
        var report = await new WindowsHardwareMediaCapabilityProbe()
            .ProbeAsync(CancellationToken.None);

        Assert.Empty(report.HardwareDecodeCodecs);
        Assert.Empty(report.HardwareEncodeCodecs);
        Assert.False(report.AcceptsGpuSurfaceInput);
        Assert.False(report.RequiresCpuStaging);
        Assert.Equal(GpuExportProofStatus.Pending, report.ExportProofStatus);
        Assert.Contains("not completed", report.ExportProofReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "windows.mf.d3d11va.decode.h264" &&
            backend.SupportStatus == MediaForgeSupportStatus.PrototypeOnly &&
            backend.ProductReadinessStatus == MediaForgeProductReadinessStatus.Prototype);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "windows.mf.hardware_mft.encode.h264" &&
            backend.SupportStatus == MediaForgeSupportStatus.PrototypeOnly &&
            backend.ProductReadinessStatus == MediaForgeProductReadinessStatus.Prototype);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "linux.vaapi.drm.decode_encode" &&
            backend.SupportStatus == MediaForgeSupportStatus.Planned);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "macos.videotoolbox.decode_encode" &&
            backend.SupportStatus == MediaForgeSupportStatus.Planned);
    }

    [Fact]
    public void Media_foundation_encoder_probe_returns_empty_until_real_mft_output_validation_lands()
    {
        var probe = new MediaFoundationHardwareEncoderProbe();

        Assert.Empty(probe.Probe());
    }

    [Fact]
    public void Windows_video_decoder_probe_returns_empty_until_real_gpu_decode_lands()
    {
        var probe = new WindowsHardwareVideoDecoderProbe();

        Assert.Empty(probe.Probe());
    }
}
