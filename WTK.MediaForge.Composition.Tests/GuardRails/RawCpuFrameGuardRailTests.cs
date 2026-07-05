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
}
