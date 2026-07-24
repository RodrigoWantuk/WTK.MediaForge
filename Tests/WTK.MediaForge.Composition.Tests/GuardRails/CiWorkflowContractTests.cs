using Xunit;

namespace WTK.MediaForge.Composition.Tests.GuardRails;

public sealed class CiWorkflowContractTests
{
    [Fact]
    public void Hosted_ci_runs_portable_tests_and_reserves_strict_media_for_rx580_runner()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "ci.yml");
        var workflow = File.ReadAllText(workflowPath);
        var hardwareJobIndex = workflow.IndexOf("  hardware-media-rx580:", StringComparison.Ordinal);

        Assert.True(hardwareJobIndex > 0, "CI workflow must define the RX 580 hardware-media job.");

        var hostedJob = workflow[..hardwareJobIndex];
        var hardwareJob = workflow[hardwareJobIndex..];

        Assert.Contains("Category!=GPU&Category!=Stress&Category!=Performance", hostedJob, StringComparison.Ordinal);
        Assert.Contains("--no-build", hostedJob, StringComparison.Ordinal);
        Assert.DoesNotContain("-RequireHardwareMedia", hostedJob, StringComparison.Ordinal);
        Assert.Contains("runs-on: [self-hosted, Windows, mediaforge-rx580]", hardwareJob, StringComparison.Ordinal);
        Assert.Contains("verify-engine-readiness-v14.ps1 -RequireHardwareMedia", hardwareJob, StringComparison.Ordinal);
    }

    [Fact]
    public void Vulkan_shader_inputs_are_not_duplicated_by_default_and_explicit_globs()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "WTK.MediaForge.Graphics.Vulkan",
            "WTK.MediaForge.Graphics.Vulkan.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.Contains(
            "<EnableDefaultShaderCompileItems>false</EnableDefaultShaderCompileItems>",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<ShaderCompile Include=\"Shaders/**/*.vert\" />", project, StringComparison.Ordinal);
        Assert.Contains("<ShaderCompile Include=\"Shaders/**/*.frag\" />", project, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for CI workflow contract test.");
    }
}
