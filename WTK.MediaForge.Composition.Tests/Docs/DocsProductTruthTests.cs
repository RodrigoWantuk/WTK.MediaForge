using Xunit;

namespace WTK.MediaForge.Composition.Tests.Docs;

public sealed class DocsProductTruthTests
{
    private static readonly string[] NormativeDocs =
    [
        "README.md",
        "ARCHITECTURE.md",
        Path.Combine("docs", "AI_CONTEXT.md"),
        Path.Combine("docs", "GPU_MEDIA_SUPPORT_MATRIX.md"),
        Path.Combine("docs", "PUBLIC_API.md"),
        Path.Combine("docs", "REVIEW_CHECKLIST.md"),
        Path.Combine("docs", "ROADMAP_CURRENT.md")
    ];

    private static readonly string[] MojibakeMarkers =
    [
        "â€”", "â€“", "Ã§", "Ã£", "Ã¡", "Ã©", "Ã­", "Ã³", "Ãº", "Ãª", "\uFFFD"
    ];

    [Fact]
    public void Normative_docs_define_v14_as_the_only_current_readiness_gate()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in NormativeDocs)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("verify-engine-readiness-v13.ps1", text, StringComparison.Ordinal);
        }

        var roadmap = Read(root, "docs", "ROADMAP_CURRENT.md");
        var context = Read(root, "docs", "AI_CONTEXT.md");
        Assert.Contains("verify-engine-readiness-v14.ps1", roadmap, StringComparison.Ordinal);
        Assert.Contains("only current engine readiness entrypoint", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify-engine-readiness-v14.ps1", context, StringComparison.Ordinal);
        Assert.Contains("docs/history/readiness-scripts", context, StringComparison.Ordinal);
    }

    [Fact]
    public void Readiness_v14_is_flat_and_runs_required_current_gates()
    {
        var root = FindRepositoryRoot();
        var script = Read(root, "scripts", "verify-engine-readiness-v14.ps1");

        Assert.Contains("dotnet restore", script, StringComparison.Ordinal);
        Assert.Contains("--locked-mode", script, StringComparison.Ordinal);
        Assert.Contains("dotnet build", script, StringComparison.Ordinal);
        Assert.Contains("dotnet test", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Fast", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Gpu", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Performance", script, StringComparison.Ordinal);
        Assert.Contains("verify-media-transport-rules.ps1", script, StringComparison.Ordinal);
        Assert.Contains("verify-license-policy.ps1", script, StringComparison.Ordinal);
        Assert.Contains("generate-media-proof-report.ps1", script, StringComparison.Ordinal);
        Assert.Contains("RequireHardwareMedia", script, StringComparison.Ordinal);
        Assert.Contains("engine-readiness-v14.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-engine-readiness-v12.ps1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("verify-engine-readiness-v11.ps1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Historical_readiness_scripts_are_not_in_executable_scripts_directory()
    {
        var root = FindRepositoryRoot();
        for (var version = 4; version <= 12; version++)
        {
            Assert.False(File.Exists(Path.Combine(root, "scripts", $"verify-engine-readiness-v{version}.ps1")));
            Assert.True(File.Exists(Path.Combine(
                root,
                "docs",
                "history",
                "readiness-scripts",
                $"verify-engine-readiness-v{version}.ps1")));
        }

        Assert.False(File.Exists(Path.Combine(root, "scripts", "verify-phase2-readiness.ps1")));
        Assert.False(File.Exists(Path.Combine(root, "scripts", "verify-product-boundary.ps1")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "docs",
            "history",
            "readiness-scripts",
            "verify-phase2-readiness.ps1")));
        Assert.True(File.Exists(Path.Combine(
            root,
            "docs",
            "history",
            "readiness-scripts",
            "verify-product-boundary.ps1")));
    }

    [Fact]
    public void Product_docs_require_hardware_media_without_software_fallback()
    {
        var root = FindRepositoryRoot();
        var context = Read(root, "docs", "AI_CONTEXT.md");
        var roadmap = Read(root, "docs", "ROADMAP_CURRENT.md");
        var matrix = Read(root, "docs", "GPU_MEDIA_SUPPORT_MATRIX.md");

        Assert.Contains("Continuous video decode and encode must use platform hardware acceleration", context, StringComparison.Ordinal);
        Assert.Contains("never fall back to software decode/encode", roadmap, StringComparison.Ordinal);
        Assert.Contains("Hardware acceleration is mandatory for continuous video decode and encode", matrix, StringComparison.Ordinal);
        Assert.Contains("BackendOutputValidated", matrix, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_is_experimental_until_hosted_lifetime_gate_passes()
    {
        var root = FindRepositoryRoot();
        var roadmap = Read(root, "docs", "ROADMAP_CURRENT.md");
        var matrix = Read(root, "docs", "GPU_MEDIA_SUPPORT_MATRIX.md");
        var acceptance = Read(root, "docs", "PREVIEW_PANEL_ACCEPTANCE.md");

        Assert.Contains("PreviewPanelSink", roadmap, StringComparison.Ordinal);
        Assert.Contains("experimental", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("| Preview panel | GpuSurface | Experimental |", matrix, StringComparison.Ordinal);
        Assert.Contains("Remaining Product Gate", acceptance, StringComparison.Ordinal);
        Assert.Contains("no CPU readback", acceptance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Performance_docs_and_tier_reject_delay_only_evidence()
    {
        var root = FindRepositoryRoot();
        var baseline = Read(root, "docs", "PERFORMANCE_BASELINE.md");
        var testScript = Read(root, "scripts", "test.ps1");

        Assert.Contains("real engine/runtime work", baseline, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Task.Delay", baseline, StringComparison.Ordinal);
        Assert.Contains("-RequireTests", testScript, StringComparison.Ordinal);
        Assert.Contains("$compositionTests", testScript, StringComparison.Ordinal);
        Assert.Contains("$vulkanTests", testScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$diagnosticsTests -Filter $performanceFilter", testScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Normative_docs_do_not_contain_mvp_or_mojibake()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in NormativeDocs)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("MVP", text, StringComparison.OrdinalIgnoreCase);
            foreach (var marker in MojibakeMarkers)
                Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
        }
    }

    private static string Read(string root, params string[] pathParts) =>
        File.ReadAllText(pathParts.Aggregate(root, Path.Combine));

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
