using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Windows.Media.Proofs;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class WindowsHardwareH264EncodeProofRunner : HardwareMediaProofRunner
{
    public WindowsHardwareH264EncodeProofRunner()
        : base(MediaForgeCapabilityCatalog.HardwareEncodeProof, "Windows hardware H.264 encode proof")
    {
    }

    public override async ValueTask<HardwareMediaProofResult> RunAsync(
        HardwareMediaCapabilityReport baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Unavailable("Windows Media Foundation hardware encode proof requires Windows.", "MediaFoundation-HardwareMft");

        try
        {
            var result = await WindowsRenderedOutputH264ProofPipeline
                .RunSustainedCachedAsync(cancellationToken)
                .ConfigureAwait(false);

            return Passed(
                "MediaFoundation-HardwareMft",
                [
                    nameof(MediaTransportAuditEvidenceKind.BackendOutputValidated),
                    "D3D11SurfaceInput",
                    "GpuFormatConversion",
                    "H264Packet"
                ],
                baseline.GpuVendor,
                $"Hardware encoder produced {result.Packets.Count} backend-validated H.264 packet(s) from sustained rendered GPU surfaces.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unavailable(
                $"Media Foundation hardware H.264 encode proof unavailable: {ex.Message}",
                "MediaFoundation-HardwareMft",
                baseline.GpuVendor);
        }
    }
}
