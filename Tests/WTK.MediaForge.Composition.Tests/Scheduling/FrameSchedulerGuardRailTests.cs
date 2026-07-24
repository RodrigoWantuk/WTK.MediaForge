using WTK.MediaForge.Composition.Outputs;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class FrameSchedulerGuardRailTests
{
    [Fact]
    public void Sink_source_files_do_not_invoke_render_publish()
    {
        var compositionRoot = Path.Combine(FindRepositoryRoot(), "WTK.MediaForge.Composition");

        var outputsDir = Path.Combine(compositionRoot, "Outputs");
        Assert.True(Directory.Exists(outputsDir), $"Outputs directory not found: {outputsDir}");

        var forbidden = new[]
        {
            "PublishFrame(",
            "MediaForgeRenderThread",
            ".Submit(",
            "FrameScheduler"
        };

        foreach (var file in Directory.EnumerateFiles(outputsDir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(
                    token,
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WTK.MediaForge.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate WTK.MediaForge.sln from the test output directory.");
    }
}
