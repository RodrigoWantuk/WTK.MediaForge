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
            Assert.Contains("Overall status: Blocked", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Release gate passed: False", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            using var json = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(directory, "media-proof-report.json")));
            Assert.Equal(
                nameof(HardwareMediaValidationStatus.Blocked),
                json.RootElement.GetProperty("OverallStatus").GetString());
            Assert.False(json.RootElement.GetProperty("ReleaseGatePassed").GetBoolean());
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
            Assert.Contains("Hardware media release gate is blocked", error.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public async Task Sustained_tool_generates_versioned_reports_from_qualification_result()
    {
        var directory = CreateTempDirectory();
        try
        {
            var exitCode = await SustainedQualificationCommand.RunAsync(
                [
                    "--sustained-qualification",
                    "--duration-minutes", "1",
                    "--sample-seconds", "1",
                    "--out", directory
                ],
                (request, _) => ValueTask.FromResult(CreateSustainedReport(request)),
                TextWriter.Null,
                TextWriter.Null);

            Assert.Equal(0, exitCode);
            var jsonPath = Path.Combine(directory, "sustained-media-qualification.json");
            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(Path.Combine(directory, "sustained-media-qualification.md")));
            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.Equal(
                SustainedQualificationReport.CurrentSchemaVersion,
                json.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Sustained_tool_blocks_when_memory_growth_exceeds_threshold()
    {
        var directory = CreateTempDirectory();
        try
        {
            var error = new StringWriter();
            var exitCode = await SustainedQualificationCommand.RunAsync(
                [
                    "--sustained-qualification",
                    "--duration-minutes", "1",
                    "--sample-seconds", "1",
                    "--max-memory-growth-mb", "1",
                    "--out", directory
                ],
                (request, _) => ValueTask.FromResult(
                    CreateSustainedReport(request) with
                    {
                        PeakPrivateMemoryBytes = 4 * 1024 * 1024,
                        PrivateMemoryGrowthBytes = 4 * 1024 * 1024
                    }),
                TextWriter.Null,
                error);

            Assert.Equal(2, exitCode);
            Assert.Contains("memory growth", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Sustained_tool_reports_nested_cleanup_failures()
    {
        var directory = CreateTempDirectory();
        try
        {
            var error = new StringWriter();
            var exitCode = await SustainedQualificationCommand.RunAsync(
                ["--sustained-qualification", "--out", directory],
                (_, _) => ValueTask.FromException<SustainedQualificationReport>(
                    new InvalidOperationException(
                        "Engine cleanup failed.",
                        new AggregateException(
                            new TimeoutException("Encoder worker did not stop."),
                            new InvalidOperationException("Backend lease remained active.")))),
                TextWriter.Null,
                error);

            Assert.Equal(1, exitCode);
            Assert.Contains("Encoder worker did not stop", error.ToString(), StringComparison.Ordinal);
            Assert.Contains("Backend lease remained active", error.ToString(), StringComparison.Ordinal);
            var json = File.ReadAllText(Path.Combine(directory, "sustained-media-qualification.json"));
            Assert.Contains("Encoder worker did not stop", json, StringComparison.Ordinal);
            Assert.Contains("Backend lease remained active", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Sustained_release_candidate_uses_eight_hour_duration()
    {
        var options = SustainedQualificationOptions.Parse(
            ["--sustained-qualification", "--release-candidate"]);

        Assert.Equal(TimeSpan.FromHours(8), options.Duration);
        Assert.Equal(1920, options.Width);
        Assert.Equal(1080, options.Height);
        Assert.Equal(60, options.FramesPerSecond);
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

    private static SustainedQualificationReport CreateSustainedReport(
        SustainedQualificationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        return new SustainedQualificationReport
        {
            Status = "Passed",
            StartedAt = now,
            CompletedAt = now + request.Duration,
            RequestedDurationSeconds = request.Duration.TotalSeconds,
            ActualDurationSeconds = request.Duration.TotalSeconds,
            Width = request.Width,
            Height = request.Height,
            FramesPerSecond = request.FramesPerSecond,
            BaselinePrivateMemoryBytes = 0,
            PeakPrivateMemoryBytes = 0,
            PrivateMemoryGrowthBytes = 0,
            PostStopPrivateMemoryBytes = 0,
            PostStopPrivateMemoryDeltaBytes = 0,
            BaselineHandleCount = 100,
            PeakHandleCount = 100,
            HandleGrowth = 0,
            PostStopHandleCount = 100,
            PostStopHandleDelta = 0,
            Mp4FileBytes = 1024,
            RtmpVideoPacketCount = 60
        };
    }
}
