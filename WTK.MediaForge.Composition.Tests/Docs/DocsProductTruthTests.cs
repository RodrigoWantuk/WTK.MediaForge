using Xunit;

namespace WTK.MediaForge.Composition.Tests.Docs;

public sealed class DocsProductTruthTests
{
    private static readonly string[] EngineTruthDocs =
    [
        "README.md",
        Path.Combine("docs", "AI_CONTEXT.md"),
        Path.Combine("docs", "FULL_PIPELINE_ROADMAP.md"),
        Path.Combine("docs", "GPU_MEDIA_SUPPORT_MATRIX.md"),
        Path.Combine("docs", "MEDIA_LICENSE_POLICY.md"),
        Path.Combine("docs", "PERFORMANCE_BASELINE.md"),
        Path.Combine("docs", "PHASE2_ACCEPTANCE.md"),
        Path.Combine("docs", "PUBLIC_API.md"),
        Path.Combine("docs", "REVIEW_CHECKLIST.md"),
        Path.Combine("docs", "ROADMAP_CURRENT.md")
    ];

    private static readonly string[] MojibakeMarkers =
    [
        "â€”",
        "â€“",
        "Ã§",
        "Ã£",
        "Ã¡",
        "Ã©",
        "Ã­",
        "Ã³",
        "Ãº",
        "Ãª",
        "\uFFFD"
    ];

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

        foreach (var relativePath in EngineTruthDocs)
        {
            var text = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            Assert.DoesNotContain("MVP", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Engine_truth_docs_have_current_readiness_labels()
    {
        var repoRoot = FindRepositoryRoot();
        var roadmap = File.ReadAllText(Path.Combine(repoRoot, "docs", "ROADMAP_CURRENT.md"));
        var phase2Acceptance = File.ReadAllText(Path.Combine(repoRoot, "docs", "PHASE2_ACCEPTANCE.md"));
        var supportMatrix = File.ReadAllText(Path.Combine(repoRoot, "docs", "GPU_MEDIA_SUPPORT_MATRIX.md"));

        Assert.Contains("RenderGraph | Done:Contract/resource bridge; not a GPU pass executor", roadmap, StringComparison.Ordinal);
        Assert.Contains("Color correction effect | Done:ProductValidated for Vulkan source-layer shader", roadmap, StringComparison.Ordinal);
        Assert.Contains("Blur effect | Done:ProductValidated for Vulkan source-layer shader/intermediate passes", roadmap, StringComparison.Ordinal);
        Assert.Contains("Text rendering | Done:ProductValidated for Windows Vulkan glyph atlas upload", roadmap, StringComparison.Ordinal);
        Assert.Contains("Output route transitions | Done:ProductValidated for Vulkan cut/fade output pass", roadmap, StringComparison.Ordinal);
        Assert.Contains("Performance validation | Done:Skeleton", roadmap, StringComparison.Ordinal);

        Assert.Contains("Windows hardware decode, Windows hardware encode, MP4 recording, and RTMP", phase2Acceptance, StringComparison.Ordinal);
        Assert.Contains("output are `Done:Prototype`", phase2Acceptance, StringComparison.Ordinal);

        Assert.Contains("| Video file MP4 | EncodedPacket -> GpuSurface | PrototypeOnly |", supportMatrix, StringComparison.Ordinal);
        Assert.Contains("| Recording MP4 H.264 | EncodedPacket | PrototypeOnly |", supportMatrix, StringComparison.Ordinal);
        Assert.Contains("| RTMP H.264 | EncodedPacket | PrototypeOnly |", supportMatrix, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_truth_docs_require_hardware_decode_encode_without_software_fallback()
    {
        var repoRoot = FindRepositoryRoot();
        var aiContext = File.ReadAllText(Path.Combine(repoRoot, "docs", "AI_CONTEXT.md"));
        var roadmap = File.ReadAllText(Path.Combine(repoRoot, "docs", "ROADMAP_CURRENT.md"));
        var supportMatrix = File.ReadAllText(Path.Combine(repoRoot, "docs", "GPU_MEDIA_SUPPORT_MATRIX.md"));
        var publicApi = File.ReadAllText(Path.Combine(repoRoot, "docs", "PUBLIC_API.md"));

        Assert.Contains("Continuous video decode and encode must use platform hardware acceleration", aiContext, StringComparison.Ordinal);
        Assert.Contains("Do not use software decode/encode fallback for continuous video", roadmap, StringComparison.Ordinal);
        Assert.Contains("Hardware acceleration is mandatory for continuous video decode and encode", supportMatrix, StringComparison.Ordinal);
        Assert.Contains("HardwareMediaBackendCapability", publicApi, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_readiness_v5_script_runs_required_gates()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "verify-engine-readiness-v5.ps1"));
        var testScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "test.ps1"));

        Assert.Contains("verify-media-transport-rules.ps1", script, StringComparison.Ordinal);
        Assert.Contains("verify-license-policy.ps1", script, StringComparison.Ordinal);
        Assert.Contains("ProductMediaPathsDoNotUsePrototypeEvidenceTests", script, StringComparison.Ordinal);
        Assert.Contains("ProductReadinessStatusTests", script, StringComparison.Ordinal);
        Assert.Contains("CapabilityReportTests", script, StringComparison.Ordinal);
        Assert.Contains("DocsProductTruthTests", script, StringComparison.Ordinal);
        Assert.Contains("WindowsGpuExportEndToEndProofTests", script, StringComparison.Ordinal);
        Assert.Contains("RenderedOutputEncodeFrameAdapterTests", script, StringComparison.Ordinal);
        Assert.Contains("WindowsSystemDrawingFontAtlasRasterizerTests", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Fast", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Gpu", script, StringComparison.Ordinal);
        Assert.Contains("-Tier Performance", script, StringComparison.Ordinal);
        Assert.Contains("RunConfiguration.MaxCpuCount=1", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestProject -Project $diagnosticsTests -Filter $performanceFilter -RequireTests", testScript, StringComparison.Ordinal);
        Assert.Contains("Invoke-TestProject -Project $compositionTests -Filter $performanceFilter -RequireTests", testScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_panel_acceptance_keeps_sink_experimental_until_local_reliability_gate()
    {
        var repoRoot = FindRepositoryRoot();
        var roadmap = File.ReadAllText(Path.Combine(repoRoot, "docs", "ROADMAP_CURRENT.md"));
        var acceptance = File.ReadAllText(Path.Combine(repoRoot, "docs", "PREVIEW_PANEL_ACCEPTANCE.md"));

        Assert.Contains("docs/PREVIEW_PANEL_ACCEPTANCE.md", roadmap, StringComparison.Ordinal);
        Assert.Contains("PreviewPanelSink", acceptance, StringComparison.Ordinal);
        Assert.Contains("experimental GPU preview sink", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no CPU readback", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remaining Product Gate", acceptance, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_truth_docs_do_not_contain_known_mojibake_markers()
    {
        var repoRoot = FindRepositoryRoot();

        foreach (var relativePath in EngineTruthDocs)
        {
            var text = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            foreach (var marker in MojibakeMarkers)
            {
                Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
