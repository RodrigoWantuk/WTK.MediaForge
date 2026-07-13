using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class WindowsRenderToH264EncodeProofRunner : HardwareMediaProofRunner
{
    public WindowsRenderToH264EncodeProofRunner()
        : base(MediaForgeCapabilityCatalog.RenderToEncodeProof, "Windows rendered output to H.264 encode proof")
    {
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(Unavailable(
                "Windows render-to-encode proof requires Windows D3D11 and Media Foundation.",
                "Vulkan-D3D11-MediaFoundation",
                baseline.GpuVendor));
        }

        return ValueTask.FromResult(Unavailable(
            "Render-to-encode product proof requires a renderer-owned Vulkan offscreen surface reaching Media Foundation through the engine runtime; direct D3D11 test textures are intentionally rejected.",
            "Vulkan-D3D11-MediaFoundation",
            baseline.GpuVendor));
    }
}

internal sealed class WindowsMp4OutputProductProofRunner : HardwareMediaProofRunner
{
    public WindowsMp4OutputProductProofRunner()
        : base(MediaForgeCapabilityCatalog.Mp4OutputProductProof, "Windows MP4 output product proof")
    {
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Unavailable(
            "MP4 product proof requires render-to-encode and hardware H.264 encode proofs to pass before a real packet-only MP4 can be promoted.",
            "MediaFoundation-HardwareMft",
            baseline.GpuVendor));
    }
}

internal sealed class WindowsRtmpNetworkOutputProofRunner : HardwareMediaProofRunner
{
    public WindowsRtmpNetworkOutputProofRunner()
        : base(MediaForgeCapabilityCatalog.RtmpNetworkOutputProof, "Windows RTMP network output proof")
    {
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Unavailable(
            "RTMP network product proof requires hardware-validated H.264 packets from the render-output encode route before network output can be promoted.",
            "TCP-RTMP",
            baseline.GpuVendor));
    }
}

internal sealed class WindowsHardwareDecodeProofRunner : HardwareMediaProofRunner
{
    public WindowsHardwareDecodeProofRunner()
        : base(MediaForgeCapabilityCatalog.HardwareDecodeProof, "Windows hardware H.264 decode proof")
    {
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(Unavailable(
                "Windows hardware decode proof requires Windows D3D11VA and Media Foundation.",
                "MediaFoundation-D3D11VA",
                baseline.GpuVendor));
        }

        return ValueTask.FromResult(Unavailable(
            "Hardware decode proof requires an approved H.264 test asset and Media Foundation output as IMFDXGIBuffer GPU textures; no CPU sample fallback is allowed.",
            "MediaFoundation-D3D11VA",
            baseline.GpuVendor));
    }
}

internal sealed class WindowsDecodeToRenderProofRunner : HardwareMediaProofRunner
{
    public WindowsDecodeToRenderProofRunner()
        : base(MediaForgeCapabilityCatalog.DecodeToRenderProof, "Windows hardware decode to renderer proof")
    {
    }

    public override ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Unavailable(
            "Decode-to-render proof requires a hardware-decoded D3D11 texture to pass through VideoSourceRuntime and be submitted by the Vulkan renderer with backend-output-validated evidence.",
            "MediaFoundation-D3D11VA-Vulkan",
            baseline.GpuVendor));
    }
}
