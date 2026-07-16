using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media.Ndi;

internal sealed class WindowsNdiInputProductProofRunner : HardwareMediaProofRunner
{
    private readonly IWindowsNdiRuntimeProbe _runtimeProbe;

    public WindowsNdiInputProductProofRunner(IWindowsNdiRuntimeProbe? runtimeProbe = null)
        : base(MediaForgeCapabilityCatalog.NdiInputProductProof, "Windows NDI input product proof")
    {
        _runtimeProbe = runtimeProbe ?? new WindowsNdiRuntimeProbe();
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        var runtime = _runtimeProbe.Probe();
        return ValueTask.FromResult(CreateUnavailableResult(
            runtime,
            baseline.GpuVendor,
            "input",
            "NDI receive must produce a GPU-importable surface or a hardware-decoded/encoded transport without continuous raw CPU video frames."));
    }

    private HardwareMediaProofResult CreateUnavailableResult(
        WindowsNdiRuntimeInfo runtime,
        string? vendor,
        string direction,
        string requirement)
    {
        if (!runtime.CanUseStandardSdk)
        {
            return Unavailable(
                $"NDI {direction} product proof unavailable. {runtime.Reason}",
                "NDI-SDK",
                vendor);
        }

        return Unavailable(
            $"NDI runtime detected at '{runtime.LibraryPath}'{FormatVersion(runtime)}. {requirement} The Standard SDK runtime alone is not accepted as a MediaForge product path because it exposes frame-buffer APIs that would require continuous CPU/RAM video transport.",
            "NDI-SDK",
            vendor);
    }

    private static string FormatVersion(WindowsNdiRuntimeInfo runtime) =>
        string.IsNullOrWhiteSpace(runtime.Version)
            ? string.Empty
            : $" ({runtime.Version})";
}

internal sealed class WindowsNdiOutputProductProofRunner : HardwareMediaProofRunner
{
    private readonly IWindowsNdiRuntimeProbe _runtimeProbe;

    public WindowsNdiOutputProductProofRunner(IWindowsNdiRuntimeProbe? runtimeProbe = null)
        : base(MediaForgeCapabilityCatalog.NdiOutputProductProof, "Windows NDI output product proof")
    {
        _runtimeProbe = runtimeProbe ?? new WindowsNdiRuntimeProbe();
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        var runtime = _runtimeProbe.Probe();
        if (!runtime.CanUseStandardSdk)
        {
            return ValueTask.FromResult(Unavailable(
                $"NDI output product proof unavailable. {runtime.Reason}",
                "NDI-SDK",
                baseline.GpuVendor));
        }

        return ValueTask.FromResult(Unavailable(
            $"NDI runtime detected at '{runtime.LibraryPath}'{FormatVersion(runtime)}. NDI output must consume a rendered GPU surface or hardware encoded packets without continuous CPU readback. The Standard SDK runtime alone is not accepted as a MediaForge product path because High Bandwidth send requires frame-buffer access.",
            "NDI-SDK",
            baseline.GpuVendor));
    }

    private static string FormatVersion(WindowsNdiRuntimeInfo runtime) =>
        string.IsNullOrWhiteSpace(runtime.Version)
            ? string.Empty
            : $" ({runtime.Version})";
}
