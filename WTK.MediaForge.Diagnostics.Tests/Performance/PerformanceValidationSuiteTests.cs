using WTK.MediaForge.Diagnostics.Tests.Performance;
using Xunit;

namespace WTK.MediaForge.Diagnostics.Tests.Performance;

[Trait("Category", "Performance")]
public sealed class PerformanceValidationSuiteTests
{
    [Fact]
    public async Task Performance_suite_generates_json_and_markdown_artifacts()
    {
        var suite = new PerformanceValidationSuite();
        var reports = await suite.RunAllAsync();

        Assert.Equal(4, reports.Count);
        Assert.All(reports, report => Assert.True(report.Duration > TimeSpan.Zero));

        var repoRoot = FindRepoRoot();
        await PerformanceValidationSuite.WriteArtifactsAsync(reports, repoRoot);

        var outputDir = Path.Combine(repoRoot, "artifacts", "performance");
        Assert.True(Directory.Exists(outputDir));
        Assert.NotEmpty(Directory.GetFiles(outputDir, "performance_*.json"));
        Assert.NotEmpty(Directory.GetFiles(outputDir, "performance_*.md"));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WTK.MediaForge.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
