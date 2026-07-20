using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Windows.Media.Qualification;

namespace WTK.MediaForge.Tools.MediaProofReport;

public sealed record SustainedQualificationRequest(
    TimeSpan Duration,
    int Width,
    int Height,
    int FramesPerSecond,
    TimeSpan SampleInterval);

public sealed record SustainedQualificationOutputReport(
    string OutputId,
    EncodedOutputRuntimeStatus Status,
    string? Reason,
    long FramesSubmitted,
    long PacketsProduced,
    long PacketsWritten,
    long FramesDropped,
    double LastPacketLatencyMilliseconds);

public sealed record SustainedQualificationReport
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required double RequestedDurationSeconds { get; init; }

    public required double ActualDurationSeconds { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int FramesPerSecond { get; init; }

    public required long BaselinePrivateMemoryBytes { get; init; }

    public required long PeakPrivateMemoryBytes { get; init; }

    public required long PrivateMemoryGrowthBytes { get; init; }

    public required long PostStopPrivateMemoryBytes { get; init; }

    public required long PostStopPrivateMemoryDeltaBytes { get; init; }

    public required int BaselineHandleCount { get; init; }

    public required int PeakHandleCount { get; init; }

    public required int HandleGrowth { get; init; }

    public required int PostStopHandleCount { get; init; }

    public required int PostStopHandleDelta { get; init; }

    public required long Mp4FileBytes { get; init; }

    public required int RtmpVideoPacketCount { get; init; }

    public int ErrorDiagnosticCount { get; init; }

    public int FatalDiagnosticCount { get; init; }

    public string? FailureReason { get; init; }

    public IReadOnlyList<SustainedQualificationOutputReport> Outputs { get; init; } =
        Array.Empty<SustainedQualificationOutputReport>();
}

public static class SustainedQualificationCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(
        string[] args,
        Func<SustainedQualificationRequest, CancellationToken, ValueTask<SustainedQualificationReport>>? runner = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var options = SustainedQualificationOptions.Parse(args);
        if (options.ShowHelp)
        {
            await output.WriteLineAsync(SustainedQualificationOptions.HelpText).ConfigureAwait(false);
            return 0;
        }

        runner ??= RunWindowsAsync;
        Directory.CreateDirectory(options.OutputDirectory);
        var reportPath = Path.Combine(options.OutputDirectory, "sustained-media-qualification.json");
        var markdownPath = Path.Combine(options.OutputDirectory, "sustained-media-qualification.md");

        try
        {
            var request = new SustainedQualificationRequest(
                options.Duration,
                options.Width,
                options.Height,
                options.FramesPerSecond,
                options.SampleInterval);
            var report = await runner(request, cancellationToken).ConfigureAwait(false);
            var thresholdFailure = ValidateThresholds(report, options);
            if (thresholdFailure is not null)
                report = report with { Status = "Failed", FailureReason = thresholdFailure };

            await WriteReportsAsync(report, reportPath, markdownPath, cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync($"Sustained media qualification: {report.Status}").ConfigureAwait(false);
            await output.WriteLineAsync($"JSON: {Path.GetFullPath(reportPath)}").ConfigureAwait(false);
            await output.WriteLineAsync($"Markdown: {Path.GetFullPath(markdownPath)}").ConfigureAwait(false);

            if (!report.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase))
            {
                await error.WriteLineAsync(report.FailureReason ?? "Sustained media qualification failed.")
                    .ConfigureAwait(false);
                return 2;
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var now = DateTimeOffset.UtcNow;
            var report = new SustainedQualificationReport
            {
                Status = "Failed",
                StartedAt = now,
                CompletedAt = now,
                RequestedDurationSeconds = options.Duration.TotalSeconds,
                ActualDurationSeconds = 0,
                Width = options.Width,
                Height = options.Height,
                FramesPerSecond = options.FramesPerSecond,
                BaselinePrivateMemoryBytes = 0,
                PeakPrivateMemoryBytes = 0,
                PrivateMemoryGrowthBytes = 0,
                PostStopPrivateMemoryBytes = 0,
                PostStopPrivateMemoryDeltaBytes = 0,
                BaselineHandleCount = 0,
                PeakHandleCount = 0,
                HandleGrowth = 0,
                PostStopHandleCount = 0,
                PostStopHandleDelta = 0,
                Mp4FileBytes = 0,
                RtmpVideoPacketCount = 0,
                FailureReason = ex.Message
            };
            await WriteReportsAsync(report, reportPath, markdownPath, CancellationToken.None).ConfigureAwait(false);
            await error.WriteLineAsync($"Sustained media qualification failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async ValueTask<SustainedQualificationReport> RunWindowsAsync(
        SustainedQualificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await WindowsSustainedMediaQualificationRunner.RunAsync(
                new WindowsSustainedMediaQualificationOptions(
                    request.Duration,
                    request.Width,
                    request.Height,
                    request.FramesPerSecond,
                    request.SampleInterval),
                cancellationToken)
            .ConfigureAwait(false);
        var outputs = result.Outputs.Select(output => new SustainedQualificationOutputReport(
            output.OutputId.ToString(),
            output.Status,
            output.Reason,
            output.FramesSubmitted,
            output.PacketsProduced,
            output.PacketsWritten,
            output.FramesDropped,
            output.LastPacketLatency.TotalMilliseconds)).ToArray();

        return new SustainedQualificationReport
        {
            Status = "Passed",
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            RequestedDurationSeconds = request.Duration.TotalSeconds,
            ActualDurationSeconds = result.CompletedAt.Subtract(result.StartedAt).TotalSeconds,
            Width = result.Width,
            Height = result.Height,
            FramesPerSecond = result.FramesPerSecond,
            BaselinePrivateMemoryBytes = result.BaselinePrivateMemoryBytes,
            PeakPrivateMemoryBytes = result.PeakPrivateMemoryBytes,
            PrivateMemoryGrowthBytes = Math.Max(0, result.PeakPrivateMemoryBytes - result.BaselinePrivateMemoryBytes),
            PostStopPrivateMemoryBytes = result.PostStopPrivateMemoryBytes,
            PostStopPrivateMemoryDeltaBytes = Math.Max(
                0,
                result.PostStopPrivateMemoryBytes - result.BaselinePrivateMemoryBytes),
            BaselineHandleCount = result.BaselineHandleCount,
            PeakHandleCount = result.PeakHandleCount,
            HandleGrowth = Math.Max(0, result.PeakHandleCount - result.BaselineHandleCount),
            PostStopHandleCount = result.PostStopHandleCount,
            PostStopHandleDelta = Math.Max(0, result.PostStopHandleCount - result.BaselineHandleCount),
            Mp4FileBytes = result.Mp4FileBytes,
            RtmpVideoPacketCount = result.RtmpVideoPacketCount,
            ErrorDiagnosticCount = result.Diagnostics.Count(static diagnostic =>
                diagnostic.Severity == WTK.MediaForge.Diagnostics.MediaForgeDiagnosticSeverity.Error),
            FatalDiagnosticCount = result.Diagnostics.Count(static diagnostic =>
                diagnostic.Severity == WTK.MediaForge.Diagnostics.MediaForgeDiagnosticSeverity.Fatal),
            Outputs = outputs
        };
    }

    private static string? ValidateThresholds(
        SustainedQualificationReport report,
        SustainedQualificationOptions options)
    {
        if (report.FatalDiagnosticCount > 0)
            return $"Qualification emitted {report.FatalDiagnosticCount} fatal diagnostic(s).";
        if (report.PrivateMemoryGrowthBytes > options.MaxPrivateMemoryGrowthBytes)
            return $"Private memory growth {report.PrivateMemoryGrowthBytes} exceeded {options.MaxPrivateMemoryGrowthBytes} bytes.";
        if (report.HandleGrowth > options.MaxHandleGrowth)
            return $"Handle growth {report.HandleGrowth} exceeded {options.MaxHandleGrowth}.";
        if (report.PostStopPrivateMemoryDeltaBytes > options.MaxPrivateMemoryGrowthBytes)
            return $"Post-stop private memory delta {report.PostStopPrivateMemoryDeltaBytes} exceeded {options.MaxPrivateMemoryGrowthBytes} bytes.";
        if (report.PostStopHandleDelta > options.MaxHandleGrowth)
            return $"Post-stop handle delta {report.PostStopHandleDelta} exceeded {options.MaxHandleGrowth}.";
        return null;
    }

    private static async Task WriteReportsAsync(
        SustainedQualificationReport report,
        string jsonPath,
        string markdownPath,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            markdownPath,
            WriteMarkdown(report),
            cancellationToken).ConfigureAwait(false);
    }

    private static string WriteMarkdown(SustainedQualificationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Sustained Media Qualification");
        builder.AppendLine();
        builder.AppendLine($"- Status: **{report.Status}**");
        builder.AppendLine($"- Route: {report.Width}x{report.Height} at {report.FramesPerSecond} fps");
        builder.AppendLine($"- Requested duration: {report.RequestedDurationSeconds:F1} s");
        builder.AppendLine($"- Actual duration: {report.ActualDurationSeconds:F1} s");
        builder.AppendLine($"- Private memory growth: {report.PrivateMemoryGrowthBytes:N0} bytes");
        builder.AppendLine($"- Handle growth: {report.HandleGrowth}");
        builder.AppendLine($"- Post-stop private memory delta: {report.PostStopPrivateMemoryDeltaBytes:N0} bytes");
        builder.AppendLine($"- Post-stop handle delta: {report.PostStopHandleDelta}");
        builder.AppendLine($"- MP4 bytes: {report.Mp4FileBytes:N0}");
        builder.AppendLine($"- RTMP video packets: {report.RtmpVideoPacketCount:N0}");
        if (!string.IsNullOrWhiteSpace(report.FailureReason))
            builder.AppendLine($"- Failure: {report.FailureReason}");
        builder.AppendLine();
        builder.AppendLine("| Output | Status | Submitted | Produced | Written | Dropped |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var output in report.Outputs)
        {
            builder.AppendLine(
                $"| {output.OutputId} | {output.Status} | {output.FramesSubmitted} | {output.PacketsProduced} | {output.PacketsWritten} | {output.FramesDropped} |");
        }

        return builder.ToString();
    }
}

public sealed class SustainedQualificationOptions
{
    public string OutputDirectory { get; private init; } = "test-reports";
    public TimeSpan Duration { get; private init; } = TimeSpan.FromMinutes(30);
    public int Width { get; private init; } = 1920;
    public int Height { get; private init; } = 1080;
    public int FramesPerSecond { get; private init; } = 60;
    public TimeSpan SampleInterval { get; private init; } = TimeSpan.FromSeconds(5);
    public long MaxPrivateMemoryGrowthBytes { get; private init; } = 512L * 1024 * 1024;
    public int MaxHandleGrowth { get; private init; } = 256;
    public bool ShowHelp { get; private init; }

    public const string HelpText =
        """
        WTK MediaForge sustained media qualification

        Options:
          --sustained-qualification       Run the real preview+MP4+RTMP route
          --duration-minutes <number>     Duration in minutes. Default: 30
          --release-candidate             Use the 8-hour release-candidate duration
          --width <pixels>                Output width. Default: 1920
          --height <pixels>               Output height. Default: 1080
          --fps <number>                  Frame rate. Default: 60
          --sample-seconds <number>       Resource sample interval. Default: 5
          --max-memory-growth-mb <number> Maximum private-memory growth. Default: 512
          --max-handle-growth <number>    Maximum process handle growth. Default: 256
          --out <directory>               Report directory. Default: test-reports
        """;

    public static SustainedQualificationOptions Parse(IReadOnlyList<string> args)
    {
        var duration = TimeSpan.FromMinutes(30);
        var width = 1920;
        var height = 1080;
        var fps = 60;
        var sampleInterval = TimeSpan.FromSeconds(5);
        var maxMemory = 512L * 1024 * 1024;
        var maxHandles = 256;
        var outputDirectory = "test-reports";
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals("--sustained-qualification", StringComparison.OrdinalIgnoreCase))
                continue;
            if (arg.Equals("--release-candidate", StringComparison.OrdinalIgnoreCase))
            {
                duration = TimeSpan.FromHours(8);
                continue;
            }
            if (arg is "--help" or "-h")
            {
                showHelp = true;
                continue;
            }

            var value = ReadValue(args, ref index, arg);
            switch (arg.ToLowerInvariant())
            {
                case "--duration-minutes": duration = TimeSpan.FromMinutes(ParsePositiveDouble(arg, value)); break;
                case "--width": width = ParsePositiveInt(arg, value); break;
                case "--height": height = ParsePositiveInt(arg, value); break;
                case "--fps": fps = ParsePositiveInt(arg, value); break;
                case "--sample-seconds": sampleInterval = TimeSpan.FromSeconds(ParsePositiveDouble(arg, value)); break;
                case "--max-memory-growth-mb": maxMemory = checked((long)(ParsePositiveDouble(arg, value) * 1024 * 1024)); break;
                case "--max-handle-growth": maxHandles = ParsePositiveInt(arg, value); break;
                case "--out": outputDirectory = value; break;
                default: throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (sampleInterval > duration)
            sampleInterval = duration;

        return new SustainedQualificationOptions
        {
            OutputDirectory = outputDirectory,
            Duration = duration,
            Width = width,
            Height = height,
            FramesPerSecond = fps,
            SampleInterval = sampleInterval,
            MaxPrivateMemoryGrowthBytes = maxMemory,
            MaxHandleGrowth = maxHandles,
            ShowHelp = showHelp
        };
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string name)
    {
        if (index + 1 >= args.Count)
            throw new ArgumentException($"{name} requires a value.");
        return args[++index];
    }

    private static int ParsePositiveInt(string name, string value) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} requires a positive integer.");

    private static double ParsePositiveDouble(string name, string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
        double.IsFinite(parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"{name} requires a positive number.");
}
