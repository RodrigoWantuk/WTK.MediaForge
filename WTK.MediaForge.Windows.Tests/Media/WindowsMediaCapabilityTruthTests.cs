using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Composition.Outputs;
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
    public async Task Windows_capability_probe_reports_v8_media_proofs_with_explicit_reasons()
    {
        var report = await new WindowsHardwareMediaCapabilityProbe()
            .ProbeAsync(CancellationToken.None);

        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.RenderToEncodeProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.HardwareEncodeProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.Mp4RecordingProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.HardwareDecodeProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.DecodeToRenderProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.Mp4OutputProductProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.Mp4InputProductProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.WebcamInputProductProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.RtmpNetworkOutputProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.NdiInputProductProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
        Assert.Contains(report.Proofs, proof =>
            proof.Id == MediaForgeCapabilityCatalog.NdiOutputProductProof &&
            proof.Status == HardwareMediaProofStatus.Unavailable &&
            !string.IsNullOrWhiteSpace(proof.Reason));
    }

    [Fact]
    public async Task Required_hardware_media_release_gate_fails_until_all_v8_proofs_pass()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        var report = await new WindowsHardwareMediaCapabilityProbe()
            .ProbeAsync(CancellationToken.None);
        var missing = report.Proofs
            .Where(static proof => proof.Status != HardwareMediaProofStatus.Passed)
            .Select(static proof => $"{proof.Id}: {proof.Status} - {proof.Reason}")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Hardware media release gate requires all v8 proofs to pass: " + string.Join("; ", missing));
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

    [Fact]
    public async Task Public_windows_capability_report_includes_media_io_outputs_as_unavailable()
    {
        var report = await MediaForgeWindows.GetCapabilityReportAsync(CancellationToken.None);

        var mp4 = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry($"output.{RenderOutputTypes.RecordingMp4.Value}"));
        Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, mp4.SupportStatus);
        Assert.False(report.IsFeatureAvailable(mp4.Id));

        var rtmp = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry($"output.{RenderOutputTypes.StreamingRtmp.Value}"));
        Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, rtmp.SupportStatus);
        Assert.False(report.IsFeatureAvailable(rtmp.Id));

        var ndi = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry($"output.{RenderOutputTypes.Ndi.Value}"));
        Assert.Equal(MediaForgeSupportStatus.Unsupported, ndi.SupportStatus);
        Assert.False(report.IsFeatureAvailable(ndi.Id));
    }
}
