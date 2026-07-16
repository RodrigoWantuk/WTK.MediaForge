using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows.Media.Ndi;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media;

public sealed class WindowsNdiRuntimeProbeTests
{
    [Fact]
    public void Ndi_runtime_probe_reports_missing_runtime_with_actionable_reason()
    {
        var probe = new WindowsNdiRuntimeProbe(
            getEnvironmentVariable: _ => null,
            fileExists: _ => false,
            additionalSearchDirectories: []);

        var result = probe.Probe();

        Assert.False(result.CanUseStandardSdk);
        Assert.Contains("NDI runtime library was not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ndi_runtime_probe_reports_loadable_runtime_without_promoting_gpu_path()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), "wtk-mf-ndi-test");
        var runtimePath = Path.Combine(runtimeDirectory, "Processing.NDI.Lib.x64.dll");
        var probe = new WindowsNdiRuntimeProbe(
            getEnvironmentVariable: name => name == "NDI_RUNTIME_DIR_V6" ? runtimeDirectory : null,
            fileExists: path => string.Equals(path, runtimePath, StringComparison.OrdinalIgnoreCase),
            tryLoadLibrary: path => (true, 0, "NDI test runtime", null),
            additionalSearchDirectories: []);

        var result = probe.Probe();

        Assert.True(result.CanUseStandardSdk);
        Assert.False(result.HasProductSafeGpuPath);
        Assert.Equal(runtimePath, result.LibraryPath);
        Assert.Equal("NDI test runtime", result.Version);
    }

    [Fact]
    public async Task Ndi_product_proofs_detect_runtime_but_remain_unavailable_without_gpu_safe_path()
    {
        var probe = new FakeNdiRuntimeProbe(new WindowsNdiRuntimeInfo(
            IsRuntimePresent: true,
            IsLoadable: true,
            LibraryPath: @"C:\NDI\Processing.NDI.Lib.x64.dll",
            Version: "NDI test runtime",
            Reason: "Detected."));
        var baseline = new HardwareMediaCapabilityReport
        {
            Platform = "Windows",
            GpuVendor = "AMD/Radeon"
        };

        var input = await new WindowsNdiInputProductProofRunner(probe)
            .RunAsync(baseline, CancellationToken.None);
        var output = await new WindowsNdiOutputProductProofRunner(probe)
            .RunAsync(baseline, CancellationToken.None);

        Assert.Equal(HardwareMediaProofStatus.Unavailable, input.Status);
        Assert.Equal(HardwareMediaProofStatus.Unavailable, output.Status);
        Assert.Contains("continuous raw CPU", input.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continuous CPU readback", output.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeNdiRuntimeProbe(WindowsNdiRuntimeInfo info) : IWindowsNdiRuntimeProbe
    {
        public WindowsNdiRuntimeInfo Probe() => info;
    }
}
