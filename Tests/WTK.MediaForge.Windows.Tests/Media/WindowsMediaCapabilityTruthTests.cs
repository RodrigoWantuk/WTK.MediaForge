using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Windows.Media;
using WTK.MediaForge.Core.Media.Audit;
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
        Assert.Contains("v14 proof runners", report.ExportProofReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "windows.mf.d3d11va.decode.h264" &&
            backend.SupportStatus == MediaForgeSupportStatus.Unavailable &&
            backend.ProductReadinessStatus == MediaForgeProductReadinessStatus.Contract);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "windows.mf.hardware_mft.encode.h264" &&
            backend.SupportStatus == MediaForgeSupportStatus.Unavailable &&
            backend.ProductReadinessStatus == MediaForgeProductReadinessStatus.Contract);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "linux.vaapi.drm.decode_encode" &&
            backend.SupportStatus == MediaForgeSupportStatus.Planned);
        Assert.Contains(report.BackendCapabilities, backend =>
            backend.Id == "macos.videotoolbox.decode_encode" &&
            backend.SupportStatus == MediaForgeSupportStatus.Planned);
    }

    [Fact]
    public async Task Windows_capability_probe_reports_v14_media_proofs_with_explicit_reasons()
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
            proof.Id == MediaForgeCapabilityCatalog.WindowCaptureInputProductProof &&
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
    public async Task Required_hardware_media_release_gate_fails_until_all_v14_proofs_pass()
    {
        if (!string.Equals(
            Environment.GetEnvironmentVariable("WTK_MEDIAFORGE_REQUIRE_HARDWARE_MEDIA"),
            "1",
            StringComparison.Ordinal))
        {
            return;
        }

        var capabilityReport = await MediaForgeWindows
            .GetCapabilityReportWithHardwareProofsAsync(CancellationToken.None);
        var validation = HardwareMediaValidationReportBuilder.Build(
            capabilityReport,
            requireHardwareMedia: true);
        var missing = validation.Features
            .Where(static feature => feature.RequiredForHardwareRelease)
            .Where(static feature => feature.Status != HardwareMediaValidationStatus.Passed)
            .Select(static feature => $"{feature.Id}: {feature.Status} - {feature.Reason}")
            .ToArray();

        Assert.True(
            validation.ReleaseGatePassed && missing.Length == 0,
            "Hardware media release gate requires all required v14 proofs to pass: " + string.Join("; ", missing));
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
        Assert.Equal(MediaForgeSupportStatus.Unavailable, mp4.SupportStatus);
        Assert.False(report.IsFeatureAvailable(mp4.Id));

        var rtmp = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry($"output.{RenderOutputTypes.StreamingRtmp.Value}"));
        Assert.Equal(MediaForgeSupportStatus.Unavailable, rtmp.SupportStatus);
        Assert.False(report.IsFeatureAvailable(rtmp.Id));

        var ndi = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry($"output.{RenderOutputTypes.Ndi.Value}"));
        Assert.True(
            ndi.SupportStatus is MediaForgeSupportStatus.Unavailable or MediaForgeSupportStatus.Blocked,
            $"Unexpected NDI support status: {ndi.SupportStatus}");
        Assert.False(report.IsFeatureAvailable(ndi.Id));

        var ndiDiscovery = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.NdiSourceDiscovery));
        Assert.Equal(MediaForgeLicenseStatus.Approved, ndiDiscovery.LicenseStatus);
        Assert.True(
            ndiDiscovery.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Unavailable,
            $"Unexpected NDI discovery support status: {ndiDiscovery.SupportStatus}");
        if (ndiDiscovery.SupportStatus == MediaForgeSupportStatus.Supported)
            Assert.True(report.IsFeatureAvailable(ndiDiscovery.Id));
    }

    [Fact]
    public async Task Windows_hardware_proof_report_runs_encode_proof_without_promoting_prototype_paths()
    {
        var registry = MediaForgeWindows.CreateHardwareMediaProofRegistry();

        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.HardwareEncodeProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.RenderToEncodeProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.Mp4OutputProductProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.RtmpNetworkOutputProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.HardwareDecodeProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.DecodeToRenderProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.Mp4InputProductProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.WebcamInputProductProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.WindowCaptureInputProductProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.NdiInputProductProof);
        Assert.Contains(registry.Runners, runner =>
            runner.Id == MediaForgeCapabilityCatalog.NdiOutputProductProof);

        var report = await MediaForgeWindows.GetCapabilityReportWithHardwareProofsAsync(
            new WindowsHardwareMediaCapabilityProbe(),
            registry,
            CancellationToken.None);

        var encodeProof = Assert.IsType<CapabilityEntry>(
            report.TryGetEntry(MediaForgeCapabilityCatalog.HardwareEncodeProof));

        Assert.True(
            encodeProof.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Unavailable,
            $"Unexpected support status: {encodeProof.SupportStatus}");
        Assert.NotEqual(MediaForgeProductReadinessStatus.Prototype, encodeProof.ProductReadinessStatus);
        Assert.NotEqual(MediaForgeProductReadinessStatus.Skeleton, encodeProof.ProductReadinessStatus);

        if (HasPassedProofs(
                report.Hardware,
                MediaForgeCapabilityCatalog.RenderToEncodeProof,
                MediaForgeCapabilityCatalog.HardwareEncodeProof,
                MediaForgeCapabilityCatalog.Mp4RecordingProof,
                MediaForgeCapabilityCatalog.Mp4OutputProductProof))
        {
            var mp4 = Assert.IsType<CapabilityEntry>(
                report.TryGetEntry($"output.{RenderOutputTypes.RecordingMp4.Value}"));
            Assert.Equal(MediaForgeSupportStatus.Supported, mp4.SupportStatus);
            Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, mp4.ProductReadinessStatus);
        }

        if (HasPassedProofs(
                report.Hardware,
                MediaForgeCapabilityCatalog.HardwareDecodeProof,
                MediaForgeCapabilityCatalog.DecodeToRenderProof,
                MediaForgeCapabilityCatalog.Mp4InputProductProof))
        {
            var videoFile = Assert.IsType<CapabilityEntry>(
                report.TryGetEntry($"source.{MediaSourceTypes.VideoFile.Value}"));
            Assert.Equal(MediaForgeSupportStatus.Experimental, videoFile.SupportStatus);
            Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, videoFile.ProductReadinessStatus);
        }
    }

    [Fact]
    public async Task Windows_composite_product_proof_runners_do_not_return_placeholder_unavailable_reasons()
    {
        var baseline = new HardwareMediaCapabilityReport
        {
            Platform = OperatingSystem.IsWindows() ? "Windows" : "Non-Windows",
            GpuVendor = "TestVendor"
        };
        HardwareMediaProofRunner[] runners =
        [
            new WindowsRenderToH264EncodeProofRunner(),
            new WindowsMp4OutputProductProofRunner(),
            new WindowsRtmpNetworkOutputProofRunner(),
            new WindowsHardwareDecodeProofRunner(),
            new WindowsDecodeToRenderProofRunner(),
            new WindowsMp4InputProductProofRunner(),
            new WindowsWebcamInputProductProofRunner(),
            new WindowsWindowCaptureInputProductProofRunner()
        ];

        foreach (var runner in runners)
        {
            var result = await runner.RunAsync(baseline, CancellationToken.None);
            Assert.True(
                result.Status is HardwareMediaProofStatus.Passed or HardwareMediaProofStatus.Unavailable,
                $"Unexpected proof status for {runner.Id}: {result.Status}");

            if (result.Status == HardwareMediaProofStatus.Unavailable &&
                OperatingSystem.IsWindows())
            {
                Assert.DoesNotContain("requires render-to-encode", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("requires a renderer-owned", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("before a real packet", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("before network output", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("requires an approved", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("requires a hardware-decoded", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("not implemented", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.False(
                    string.IsNullOrWhiteSpace(result.Reason),
                    $"Unavailable proof {runner.Id} must report a concrete machine, driver, device, or API reason.");
            }

            if (result.Status == HardwareMediaProofStatus.Passed)
            {
                Assert.Contains(
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    result.Evidence);
            }
        }
    }

    private static bool HasPassedProofs(
        HardwareMediaCapabilityReport hardware,
        params string[] proofIds) =>
        proofIds.All(id => hardware.Proofs.Any(proof =>
            proof.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            proof.Status == HardwareMediaProofStatus.Passed));

    [Fact]
    public async Task Windows_encoded_output_route_factory_refuses_unvalidated_recording_route()
    {
        var output = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Recording",
            TypeId = RenderOutputTypes.RecordingMp4,
            OutputSize = new FrameSize(320, 180),
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("test.mp4"))
        };

        var report = MediaForgeCapabilityReportBuilder.Build(new HardwareMediaCapabilityReport
        {
            Platform = "Test"
        });
        var factory = new WindowsEncodedOutputRouteFactory(
            capabilityReportFactory: _ => ValueTask.FromResult(report));

        await using var runtime = new MediaPipelineRuntime();
        var exception = await Assert.ThrowsAsync<MediaForgeUnsupportedFeatureException>(async () =>
            await factory.RegisterAsync(
                new MediaForgeProject { Outputs = [output] },
                output,
                runtime,
                CancellationToken.None));

        Assert.Equal(MediaForgeCapabilityCatalog.RecordingMp4H264, exception.FeatureCode);
        Assert.Equal(0, runtime.EncodedOutputCount);
    }

    [Fact]
    public void Compatible_mp4_and_rtmp_outputs_share_the_same_surface_route()
    {
        var canvasId = CanvasId.New();
        var recording = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Recording",
            TypeId = RenderOutputTypes.RecordingMp4,
            CanvasId = canvasId,
            OutputSize = new FrameSize(1920, 1080),
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("test.mp4"))
        };
        var streaming = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Streaming",
            TypeId = RenderOutputTypes.StreamingRtmp,
            CanvasId = canvasId,
            OutputSize = recording.OutputSize,
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp("rtmp://localhost/live", "key"))
        };
        var project = new MediaForgeProject { Outputs = [recording, streaming] };
        var factory = new WindowsEncodedOutputRouteFactory();

        Assert.Equal(recording.Id, factory.ResolveSurfaceOutputId(project, recording));
        Assert.Equal(recording.Id, factory.ResolveSurfaceOutputId(project, streaming));
    }

    [Fact]
    public void Different_encoded_profiles_do_not_share_a_surface_route()
    {
        var canvasId = CanvasId.New();
        var recording = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Recording",
            TypeId = RenderOutputTypes.RecordingMp4,
            CanvasId = canvasId,
            OutputSize = new FrameSize(1920, 1080),
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("test.mp4"))
        };
        var streaming = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Streaming",
            TypeId = RenderOutputTypes.StreamingRtmp,
            CanvasId = canvasId,
            OutputSize = recording.OutputSize,
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp(
                "rtmp://localhost/live",
                "key",
                new EncodedVideoProfile { BitrateBitsPerSecond = 4_000_000 }))
        };
        var project = new MediaForgeProject { Outputs = [recording, streaming] };
        var factory = new WindowsEncodedOutputRouteFactory();

        Assert.Equal(recording.Id, factory.ResolveSurfaceOutputId(project, recording));
        Assert.Equal(streaming.Id, factory.ResolveSurfaceOutputId(project, streaming));
    }

    [Fact]
    public async Task Remote_scene_route_is_engine_known_but_cannot_bypass_physical_proof_gate()
    {
        var output = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Remote",
            TypeId = RenderOutputTypes.RemoteScene,
            CanvasId = CanvasId.New(),
            OutputSize = new FrameSize(1920, 1080),
            Settings = RenderOutputSettingsSerializer.ToJson(
                MediaForgeOutputs.RemoteScene("wss://signal.example.test", "program"))
        };
        var factory = new WindowsEncodedOutputRouteFactory(allowUnvalidatedRoutes: true);
        await using var runtime = new MediaPipelineRuntime();

        Assert.True(factory.CanCreate(RenderOutputTypes.RemoteScene));
        var exception = await Assert.ThrowsAsync<MediaForgeUnsupportedFeatureException>(() =>
            factory.RegisterAsync(
                new MediaForgeProject { Outputs = [output] },
                output,
                runtime,
                CancellationToken.None).AsTask());

        Assert.Equal(MediaForgeCapabilityCatalog.RemoteScenePublish, exception.FeatureCode);
        Assert.Equal(0, runtime.EncodedOutputCount);
    }
}
