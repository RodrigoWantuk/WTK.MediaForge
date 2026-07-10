using Xunit;

namespace WTK.MediaForge.Composition.Tests.Docs;

public sealed class DocsProductTruthTests
{
    [Fact]
    public void Docs_do_not_label_prototype_media_paths_as_product_mvp()
    {
        var repoRoot = FindRepositoryRoot();
        var phase2Acceptance = File.ReadAllText(Path.Combine(repoRoot, "docs", "PHASE2_ACCEPTANCE.md"));
        var roadmap = File.ReadAllText(Path.Combine(repoRoot, "docs", "ROADMAP_CURRENT.md"));

        Assert.DoesNotContain("Windows Hardware Decode MVP", phase2Acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("MP4 Recording MVP", phase2Acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("RTMP Output MVP", phase2Acceptance, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows Hardware Decode MVP", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("MP4 Recording MVP | Gpu | **PrototypeOnly", roadmap, StringComparison.Ordinal);
        Assert.DoesNotContain("RTMP Output MVP | Gpu | **PrototypeOnly", roadmap, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
