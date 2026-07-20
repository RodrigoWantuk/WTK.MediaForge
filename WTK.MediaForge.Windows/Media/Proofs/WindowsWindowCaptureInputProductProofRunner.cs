using Windows.Graphics.Capture;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Windows.Media.Proofs;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class WindowsWindowCaptureInputProductProofRunner : HardwareMediaProofRunner
{
    public WindowsWindowCaptureInputProductProofRunner()
        : base(
            MediaForgeCapabilityCatalog.WindowCaptureInputProductProof,
            "Windows window capture input product proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) ||
            !GraphicsCaptureSession.IsSupported())
        {
            return Unavailable(
                "Windows Graphics Capture is not supported by this Windows version or session.",
                "WindowsGraphicsCapture-D3D11+Vulkan",
                baseline.GpuVendor);
        }

        try
        {
            await using var window = await WindowsProofWindow.CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            await WindowsHardwareDecodeProofPipeline
                .SubmitWindowCaptureProviderFrameToRendererAsync(window.Handle, cancellationToken)
                .ConfigureAwait(false);

            return Passed(
                "WindowsGraphicsCapture-D3D11+Vulkan",
                [
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "GraphicsCaptureItemForWindow",
                    "Direct3D11CaptureFramePool",
                    "GpuToGpuCopy",
                    "KeepLatestGpuSlotRing",
                    "GpuSourceFrameLease",
                    "VulkanOffscreenRenderTarget"
                ],
                baseline.GpuVendor,
                "Captured a real HWND into a D3D11 GPU slot lease and rendered it through Vulkan without CPU readback.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Window capture product proof unavailable on this machine: {ex.Message}",
                "WindowsGraphicsCapture-D3D11+Vulkan",
                baseline.GpuVendor);
        }
    }
}
