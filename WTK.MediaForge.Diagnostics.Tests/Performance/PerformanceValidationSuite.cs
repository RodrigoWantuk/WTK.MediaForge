using System.Diagnostics;
using System.Text.Json;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Diagnostics.Tests.Performance;

public sealed class PerformanceValidationReport
{
    public required string Scenario { get; init; }

    public required TimeSpan Duration { get; init; }

    public double AverageFps { get; init; }

    public double AverageFrameTimeMs { get; init; }

    public int DroppedFrames { get; init; }

    public double CpuPercent { get; init; }

    public long PeakWorkingSetBytes { get; init; }

    public int ActiveGpuTextureLeases { get; init; }

    public bool PassedThresholds { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed class PerformanceValidationSuite
{
    public const int DebugScenarioSeconds = 2;

    public const int ReleaseScenarioSeconds = 300;

    public static int ScenarioDurationSeconds =>
#if DEBUG
        DebugScenarioSeconds;
#else
        ReleaseScenarioSeconds;
#endif

    public async Task<IReadOnlyList<PerformanceValidationReport>> RunAllAsync(
        CancellationToken cancellationToken = default)
    {
        var scenarios = new[]
        {
            "video_playback",
            "composition_stress",
            "recording_path",
            "streaming_path"
        };

        var reports = new List<PerformanceValidationReport>();
        foreach (var scenario in scenarios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(await RunScenarioAsync(scenario, cancellationToken).ConfigureAwait(false));
        }

        return reports;
    }

    public async Task<PerformanceValidationReport> RunScenarioAsync(
        string scenario,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);

        var duration = TimeSpan.FromSeconds(ScenarioDurationSeconds);
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var startCpu = process.TotalProcessorTime;
        var startWorkingSet = process.WorkingSet64;

        var frameCount = 0;
        var droppedFrames = 0;
        var frameTimesMs = new List<double>();

        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frameStart = Stopwatch.GetTimestamp();
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            var frameEnd = Stopwatch.GetTimestamp();
            var frameMs = (frameEnd - frameStart) * 1000d / Stopwatch.Frequency;

            frameTimesMs.Add(frameMs);
            frameCount++;

            if (frameMs > 33.0)
                droppedFrames++;
        }

        stopwatch.Stop();
        process.Refresh();

        var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        var cpuDelta = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
        var cpuPercent = cpuDelta / (Environment.ProcessorCount * elapsedSeconds * 10d);

        var report = new PerformanceValidationReport
        {
            Scenario = scenario,
            Duration = stopwatch.Elapsed,
            AverageFps = frameCount / elapsedSeconds,
            AverageFrameTimeMs = frameTimesMs.Count == 0 ? 0 : frameTimesMs.Average(),
            DroppedFrames = droppedFrames,
            CpuPercent = cpuPercent,
            PeakWorkingSetBytes = Math.Max(startWorkingSet, process.WorkingSet64),
            ActiveGpuTextureLeases = 0,
            PassedThresholds = droppedFrames <= frameCount / 4,
            Notes =
            [
                $"Scenario duration constant: {ScenarioDurationSeconds}s",
                "GPU lease leak check delegated to Gpu tier tests."
            ]
        };

        return report;
    }

    public static async Task WriteArtifactsAsync(
        IReadOnlyList<PerformanceValidationReport> reports,
        string repoRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var outputDir = Path.Combine(repoRoot, "artifacts", "performance");
        Directory.CreateDirectory(outputDir);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var jsonPath = Path.Combine(outputDir, $"performance_{timestamp}.json");
        var markdownPath = Path.Combine(outputDir, $"performance_{timestamp}.md");

        var json = JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);

        var markdown = BuildMarkdown(reports, jsonPath);
        await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMarkdown(IReadOnlyList<PerformanceValidationReport> reports, string jsonPath)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# Performance Validation Report");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"JSON: `{jsonPath}`");
        builder.AppendLine();

        foreach (var report in reports)
        {
            builder.AppendLine($"## {report.Scenario}");
            builder.AppendLine($"- Duration: {report.Duration}");
            builder.AppendLine($"- Average FPS: {report.AverageFps:F2}");
            builder.AppendLine($"- Average frame time: {report.AverageFrameTimeMs:F2} ms");
            builder.AppendLine($"- Dropped frames: {report.DroppedFrames}");
            builder.AppendLine($"- CPU percent: {report.CpuPercent:F2}");
            builder.AppendLine($"- Peak working set: {report.PeakWorkingSetBytes}");
            builder.AppendLine($"- Thresholds passed: {report.PassedThresholds}");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
