using WTK.MediaForge.Composition.Outputs;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class FrameSchedulerGuardRailTests
{
    [Fact]
    public void Sink_source_files_do_not_invoke_render_publish()
    {
        var compositionRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "WTK.MediaForge.Composition"));

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
}
