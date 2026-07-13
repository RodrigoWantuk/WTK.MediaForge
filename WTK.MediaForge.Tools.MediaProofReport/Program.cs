using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows;

namespace WTK.MediaForge.Tools.MediaProofReport;

public static class Program
{
    public static Task<int> Main(string[] args) =>
        MediaProofReportCommand.RunAsync(args);
}

public static class MediaProofReportCommand
{
    private const int SuccessExitCode = 0;
    private const int ErrorExitCode = 1;
    private const int HardwareBlockedExitCode = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(
        string[] args,
        Func<CancellationToken, ValueTask<MediaForgeCapabilityReport>>? capabilityReportFactory = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        capabilityReportFactory ??= MediaForgeWindows.GetCapabilityReportWithHardwareProofsAsync;

        try
        {
            var options = MediaProofReportOptions.Parse(args);
            if (options.ShowHelp)
            {
                await output.WriteLineAsync(MediaProofReportOptions.HelpText).ConfigureAwait(false);
                return SuccessExitCode;
            }

            if (!string.Equals(options.Platform, "windows", StringComparison.OrdinalIgnoreCase))
            {
                await error.WriteLineAsync(
                    $"Unsupported platform '{options.Platform}'. The current proof-report tool supports 'windows'.")
                    .ConfigureAwait(false);
                return ErrorExitCode;
            }

            Directory.CreateDirectory(options.OutputDirectory);

            var capabilityReport = await capabilityReportFactory(cancellationToken).ConfigureAwait(false);
            var report = HardwareMediaValidationReportBuilder.Build(
                capabilityReport,
                options.RequireHardwareMedia);

            var jsonPath = Path.Combine(options.OutputDirectory, "media-proof-report.json");
            var markdownPath = Path.Combine(options.OutputDirectory, "media-proof-report.md");

            if (options.Format is MediaProofReportFormat.Json or MediaProofReportFormat.Both)
            {
                var json = JsonSerializer.Serialize(report, JsonOptions);
                await File.WriteAllTextAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);
            }

            if (options.Format is MediaProofReportFormat.Markdown or MediaProofReportFormat.Both)
            {
                var markdown = HardwareMediaValidationReportMarkdownWriter.Write(report);
                await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken).ConfigureAwait(false);
            }

            await output.WriteLineAsync("Media proof report generated.").ConfigureAwait(false);
            if (options.Format is MediaProofReportFormat.Json or MediaProofReportFormat.Both)
                await output.WriteLineAsync($"JSON: {Path.GetFullPath(jsonPath)}").ConfigureAwait(false);
            if (options.Format is MediaProofReportFormat.Markdown or MediaProofReportFormat.Both)
                await output.WriteLineAsync($"Markdown: {Path.GetFullPath(markdownPath)}").ConfigureAwait(false);
            await output.WriteLineAsync($"Overall status: {report.OverallStatus}").ConfigureAwait(false);
            await output.WriteLineAsync($"Release gate passed: {report.ReleaseGatePassed}").ConfigureAwait(false);

            if (options.RequireHardwareMedia && !report.ReleaseGatePassed)
            {
                await error.WriteLineAsync("Hardware media release gate failed. See media proof report for blockers.")
                    .ConfigureAwait(false);
                return HardwareBlockedExitCode;
            }

            return SuccessExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await error.WriteLineAsync($"Media proof report generation failed: {ex.Message}").ConfigureAwait(false);
            return ErrorExitCode;
        }
    }
}

public sealed class MediaProofReportOptions
{
    public string OutputDirectory { get; private init; } = "test-reports";

    public MediaProofReportFormat Format { get; private init; } = MediaProofReportFormat.Both;

    public bool RequireHardwareMedia { get; private init; }

    public string Platform { get; private init; } = "windows";

    public bool ShowHelp { get; private init; }

    public const string HelpText =
        """
        WTK MediaForge media proof report

        Options:
          --out <directory>              Output directory. Default: test-reports
          --format <json|markdown|both>  Report format. Default: both
          --platform <windows>           Platform proof backend. Default: windows
          --require-hardware-media       Return exit code 2 unless release hardware proofs pass
          --help                         Show help
        """;

    public static MediaProofReportOptions Parse(IReadOnlyList<string> args)
    {
        var outputDirectory = "test-reports";
        var format = MediaProofReportFormat.Both;
        var requireHardwareMedia = false;
        var platform = "windows";
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--require-hardware-media", StringComparison.OrdinalIgnoreCase))
            {
                requireHardwareMedia = true;
                continue;
            }

            if (TryReadValue(args, ref index, "--out", arg, out var outValue))
            {
                outputDirectory = string.IsNullOrWhiteSpace(outValue)
                    ? throw new ArgumentException("--out requires a non-empty directory.")
                    : outValue;
                continue;
            }

            if (TryReadValue(args, ref index, "--format", arg, out var formatValue))
            {
                format = ParseFormat(formatValue);
                continue;
            }

            if (TryReadValue(args, ref index, "--platform", arg, out var platformValue))
            {
                platform = string.IsNullOrWhiteSpace(platformValue)
                    ? throw new ArgumentException("--platform requires a non-empty value.")
                    : platformValue;
                continue;
            }

            throw new ArgumentException($"Unknown argument '{arg}'.");
        }

        return new MediaProofReportOptions
        {
            OutputDirectory = outputDirectory,
            Format = format,
            RequireHardwareMedia = requireHardwareMedia,
            Platform = platform,
            ShowHelp = showHelp
        };
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string name,
        string current,
        out string value)
    {
        if (current.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = current[(name.Length + 1)..];
            return true;
        }

        if (!current.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }

        if (index + 1 >= args.Count)
            throw new ArgumentException($"{name} requires a value.");

        value = args[++index];
        return true;
    }

    private static MediaProofReportFormat ParseFormat(string value) =>
        value.ToLowerInvariant() switch
        {
            "json" => MediaProofReportFormat.Json,
            "markdown" => MediaProofReportFormat.Markdown,
            "both" => MediaProofReportFormat.Both,
            _ => throw new ArgumentException($"Unsupported report format '{value}'.")
        };
}

public enum MediaProofReportFormat
{
    Json,
    Markdown,
    Both
}
