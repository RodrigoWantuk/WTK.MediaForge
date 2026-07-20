using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class HardwareMediaValidationReportTests
{
    [Fact]
    public void Validation_report_rejects_non_passed_proof_without_reason()
    {
        var report = new MediaForgeCapabilityReport
        {
            Hardware = new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs =
                [
                    new HardwareMediaProof
                    {
                        Id = MediaForgeCapabilityCatalog.HardwareEncodeProof,
                        DisplayName = "Encode",
                        Status = HardwareMediaProofStatus.Unavailable
                    }
                ]
            },
            Entries = []
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HardwareMediaValidationReportBuilder.Build(report, requireHardwareMedia: false));

        Assert.Contains(MediaForgeCapabilityCatalog.HardwareEncodeProof, exception.Message, StringComparison.Ordinal);
        Assert.Contains("reason", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validation_report_blocks_mp4_recording_until_specific_mp4_recording_proof_passes()
    {
        var capabilityReport = CreateCapabilityReport(
            [
                Capability(MediaForgeCapabilityCatalog.RecordingMp4H264, "Recording", MediaForgeSupportStatus.Supported)
            ],
            [
                PassedProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
                PassedProof(MediaForgeCapabilityCatalog.RenderToEncodeProof, "BackendCallSucceeded"),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
                PassedProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof)
            ]);

        var report = HardwareMediaValidationReportBuilder.Build(capabilityReport, requireHardwareMedia: false);
        var recording = Assert.Single(report.Features, feature => feature.Id == "feature.recording.mp4.h264");

        Assert.Equal(HardwareMediaValidationStatus.Blocked, recording.Status);
        Assert.Contains(MediaForgeCapabilityCatalog.Mp4RecordingProof, recording.MissingProofIds);
        Assert.Contains(MediaForgeCapabilityCatalog.Mp4RecordingProof, recording.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_report_release_gate_passes_only_when_required_features_are_proven()
    {
        var capabilityReport = CreateCapabilityReport(
            [
                Capability(MediaForgeCapabilityCatalog.RecordingMp4H264, "Recording", MediaForgeSupportStatus.Supported),
                Capability(MediaForgeCapabilityCatalog.RtmpH264, "RTMP", MediaForgeSupportStatus.Experimental),
                Capability(MediaForgeCapabilityCatalog.VideoFileMp4, "Video file", MediaForgeSupportStatus.Experimental),
                Capability("source.wtk.source.webcam", "Webcam", MediaForgeSupportStatus.Experimental),
                Capability("source.wtk.source.desktop", "Desktop", MediaForgeSupportStatus.Experimental),
                Capability("source.wtk.source.window.capture", "Window", MediaForgeSupportStatus.Experimental),
                Capability("source.wtk.source.ndi.input", "NDI input", MediaForgeSupportStatus.Experimental),
                Capability("output.wtk.output.ndi", "NDI output", MediaForgeSupportStatus.Experimental)
            ],
            AllRequiredProofsPassed());

        var report = HardwareMediaValidationReportBuilder.Build(
            capabilityReport,
            requireHardwareMedia: true,
            generatedAtUtc: DateTimeOffset.UnixEpoch);

        Assert.True(report.ReleaseGatePassed);
        Assert.Equal(HardwareMediaValidationStatus.Passed, report.OverallStatus);
        Assert.All(
            report.Features.Where(static feature => feature.RequiredForHardwareRelease),
            feature => Assert.Equal(HardwareMediaValidationStatus.Passed, feature.Status));
        Assert.All(
            report.Features.Where(static feature => feature.Id.Contains(".ndi", StringComparison.OrdinalIgnoreCase)),
            feature => Assert.False(feature.RequiredForHardwareRelease));
    }

    [Fact]
    public void Validation_report_require_hardware_mode_lists_release_failures()
    {
        var capabilityReport = CreateCapabilityReport(
            [
                Capability(MediaForgeCapabilityCatalog.RecordingMp4H264, "Recording", MediaForgeSupportStatus.Unavailable, "Blocked in test.")
            ],
            [
                UnavailableProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
                UnavailableProof(MediaForgeCapabilityCatalog.RenderToEncodeProof),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
                UnavailableProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof)
            ]);

        var report = HardwareMediaValidationReportBuilder.Build(capabilityReport, requireHardwareMedia: true);

        Assert.False(report.ReleaseGatePassed);
        Assert.Equal(HardwareMediaValidationStatus.Failed, report.OverallStatus);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("MP4 recording", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validation_report_json_keeps_schema_version_and_string_enums()
    {
        var capabilityReport = CreateCapabilityReport([], []);
        var report = HardwareMediaValidationReportBuilder.Build(
            capabilityReport,
            requireHardwareMedia: false,
            generatedAtUtc: DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

        Assert.Contains("\"SchemaVersion\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"OverallStatus\": \"", json, StringComparison.Ordinal);
        Assert.Contains("\"Features\":", json, StringComparison.Ordinal);
        Assert.Contains("\"Proofs\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_report_markdown_lists_features_proofs_and_failures()
    {
        var capabilityReport = CreateCapabilityReport([], []);
        var report = HardwareMediaValidationReportBuilder.Build(
            capabilityReport,
            requireHardwareMedia: true,
            generatedAtUtc: DateTimeOffset.UnixEpoch);

        var markdown = HardwareMediaValidationReportMarkdownWriter.Write(report);

        Assert.Contains("# WTK MediaForge Media Proof Report", markdown, StringComparison.Ordinal);
        Assert.Contains("## Features", markdown, StringComparison.Ordinal);
        Assert.Contains("## Proofs", markdown, StringComparison.Ordinal);
        Assert.Contains("## Release Failures", markdown, StringComparison.Ordinal);
        Assert.Contains("MP4 recording product path", markdown, StringComparison.Ordinal);
        Assert.Contains("Missing proof", markdown, StringComparison.Ordinal);
    }

    private static MediaForgeCapabilityReport CreateCapabilityReport(
        IReadOnlyList<CapabilityEntry> entries,
        IReadOnlyList<HardwareMediaProof> proofs) =>
        new()
        {
            Hardware = new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs = proofs
            },
            Entries = entries
        };

    private static CapabilityEntry Capability(
        string id,
        string displayName,
        MediaForgeSupportStatus status,
        string? reason = null) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Category = CapabilityCategories.Source,
            SupportStatus = status,
            LicenseStatus = MediaForgeLicenseStatus.Approved,
            ProductReadinessStatus = status is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental
                ? MediaForgeProductReadinessStatus.ProductValidated
                : MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = status is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental
                ? null
                : reason ?? "Unavailable in unit test."
        };

    private static IReadOnlyList<HardwareMediaProof> AllRequiredProofsPassed() =>
    [
        PassedProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
        PassedProof(MediaForgeCapabilityCatalog.RenderToEncodeProof, "BackendCallSucceeded"),
        PassedProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
        PassedProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof),
        PassedProof(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof),
        PassedProof(MediaForgeCapabilityCatalog.HardwareDecodeProof),
        PassedProof(MediaForgeCapabilityCatalog.DecodeToRenderProof),
        PassedProof(MediaForgeCapabilityCatalog.Mp4InputProductProof),
        PassedProof(MediaForgeCapabilityCatalog.WebcamInputProductProof),
        PassedProof(MediaForgeCapabilityCatalog.WindowCaptureInputProductProof),
        PassedProof(MediaForgeCapabilityCatalog.NdiInputProductProof),
        PassedProof(MediaForgeCapabilityCatalog.NdiOutputProductProof)
    ];

    private static HardwareMediaProof UnavailableProof(string id) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Status = HardwareMediaProofStatus.Unavailable,
            Reason = "Unavailable in unit test."
        };

    private static HardwareMediaProof PassedProof(
        string id,
        string evidence = "BackendOutputValidated") =>
        new()
        {
            Id = id,
            DisplayName = id,
            Status = HardwareMediaProofStatus.Passed,
            Backend = "TestBackend",
            Evidence = [evidence]
        };
}
