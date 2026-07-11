using WTK.MediaForge.Composition.Outputs;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.GuardRails;

public sealed class RawCpuFrameGuardRailTests
{
    [Fact]
    public void RawCpuFrameGuardRail_detects_synthetic_violation()
    {
        const string syntheticSource =
            "namespace WTK.MediaForge.Composition.Outputs {\n" +
            "    class BadSink { byte[] pixels; CpuReadbackSink sink; }\n" +
            "}";

        var violations = RawCpuFrameGuardRailScanner.ScanSource(
            syntheticSource,
            "WTK.MediaForge.Composition.Outputs");

        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("CpuReadbackSink", StringComparison.Ordinal));
    }

    [Fact]
    public void RawCpuFrameGuardRail_ignores_allowlisted_test_namespace()
    {
        const string syntheticSource =
            "namespace WTK.MediaForge.Composition.Tests {\n" +
            "    class OkTest { CpuReadbackSink sink; }\n" +
            "}";

        var violations = RawCpuFrameGuardRailScanner.ScanSource(
            syntheticSource,
            "WTK.MediaForge.Composition.Tests");

        Assert.Empty(violations);
    }

    [Fact]
    public void RawCpuFrameGuardRail_detects_libx264_in_product_namespace()
    {
        const string syntheticSource =
            "namespace WTK.MediaForge.Composition.Media {\n" +
            "    class BadEncoder { string codec = \"libx264\"; }\n" +
            "}";

        var violations = RawCpuFrameGuardRailScanner.ScanSource(
            syntheticSource,
            "WTK.MediaForge.Composition.Media");

        Assert.Contains(violations, v => v.Contains("libx264", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Product_assemblies_do_not_place_CpuReadbackSink_outside_outputs_debug_path()
    {
        var compositionTypes = typeof(CpuReadbackSink).Assembly.GetTypes();
        var violations = RawCpuFrameGuardRailScanner.ScanAssemblyTypes(compositionTypes);

        var productViolations = violations
            .Where(v => !v.Contains("CpuReadbackSink", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(productViolations);
    }

    [Fact]
    public void Vulkan_product_project_does_not_reference_SystemDrawing()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "WTK.MediaForge.Graphics.Vulkan");
        var files = Directory.EnumerateFiles(projectRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var violations = files
            .Where(path => File.ReadAllText(path).Contains("System.Drawing", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(FindRepositoryRoot(), path))
            .ToArray();

        Assert.Empty(violations);
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
