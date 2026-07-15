using System.Text.Json;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Tools.MediaProofReport;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class MediaProofReportToolTests
{
    [Fact]
    public async Task Tool_generates_json_and_markdown_reports()
    {
        var directory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await MediaProofReportCommand.RunAsync(
                ["--out", directory, "--format", "both"],
                _ => ValueTask.FromResult(CreateBlockedReport()),
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(directory, "media-proof-report.json")));
            Assert.True(File.Exists(Path.Combine(directory, "media-proof-report.md")));
            Assert.Contains("Media proof report generated", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tool_returns_two_when_hardware_media_is_required_but_blocked()
    {
        var directory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await MediaProofReportCommand.RunAsync(
                ["--out", directory, "--require-hardware-media"],
                _ => ValueTask.FromResult(CreateBlockedReport()),
                output,
                error);

            Assert.Equal(2, exitCode);
            Assert.Contains("Hardware media release gate failed", error.ToString(), StringComparison.Ordinal);
            var json = File.ReadAllText(Path.Combine(directory, "media-proof-report.json"));
            Assert.Contains("\"ReleaseGatePassed\": false", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tool_returns_one_for_real_generation_errors()
    {
        var directory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await MediaProofReportCommand.RunAsync(
                ["--out", directory],
                _ => throw new InvalidOperationException("probe failed"),
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Contains("probe failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tool_json_report_is_valid_and_versioned()
    {
        var directory = CreateTempDirectory();
        try
        {
            var exitCode = await MediaProofReportCommand.RunAsync(
                ["--out", directory, "--format=json"],
                _ => ValueTask.FromResult(CreateBlockedReport()),
                TextWriter.Null,
                TextWriter.Null);

            Assert.Equal(0, exitCode);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "media-proof-report.json")));
            var root = json.RootElement;
            Assert.Equal(HardwareMediaValidationReport.CurrentSchemaVersion, root.GetProperty("SchemaVersion").GetInt32());
            Assert.True(root.TryGetProperty("Features", out _));
            Assert.True(root.TryGetProperty("Proofs", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WTK.MediaForge.MediaProofReportTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static MediaForgeCapabilityReport CreateBlockedReport() =>
        new()
        {
            Hardware = new HardwareMediaCapabilityReport
            {
                Platform = "Test",
                Proofs =
                [
                    UnavailableProof(MediaForgeCapabilityCatalog.HardwareEncodeProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.RenderToEncodeProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.Mp4RecordingProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.Mp4OutputProductProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.HardwareDecodeProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.DecodeToRenderProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.Mp4InputProductProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.WebcamInputProductProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.NdiInputProductProof),
                    UnavailableProof(MediaForgeCapabilityCatalog.NdiOutputProductProof)
                ]
            },
            Entries =
            [
                Capability(MediaForgeCapabilityCatalog.RecordingMp4H264, "Recording"),
                Capability(MediaForgeCapabilityCatalog.RtmpH264, "RTMP"),
                Capability(MediaForgeCapabilityCatalog.VideoFileMp4, "Video file"),
                Capability("source.wtk.source.webcam", "Webcam"),
                Capability("source.wtk.source.desktop", "Desktop", MediaForgeSupportStatus.Experimental),
                Capability("source.wtk.source.window.capture", "Window"),
                Capability("source.wtk.source.ndi.input", "NDI input"),
                Capability("output.wtk.output.ndi", "NDI output")
            ]
        };

    private static CapabilityEntry Capability(
        string id,
        string displayName,
        MediaForgeSupportStatus status = MediaForgeSupportStatus.Unavailable) =>
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
                : "Unavailable in tool test."
        };

    private static HardwareMediaProof UnavailableProof(string id) =>
        new()
        {
            Id = id,
            DisplayName = id,
            Status = HardwareMediaProofStatus.Unavailable,
            Reason = "Unavailable in tool test."
        };
}
