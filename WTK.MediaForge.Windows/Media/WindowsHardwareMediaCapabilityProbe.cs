using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media;

public sealed class WindowsHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apis = new List<string> { "D3D11", "Vulkan", "MediaFoundation" };
        var decodeCodecs = new List<string>();
        var encodeCodecs = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            decodeCodecs.Add("H264-Hardware-ProbePending");
            encodeCodecs.Add("H264-MF-HardwareMft-ProbePending");
        }

        var report = new HardwareMediaCapabilityReport
        {
            Platform = RuntimeInformation.OSDescription,
            GpuVendor = "Unknown",
            DeviceName = "Unknown",
            DetectedApis = apis,
            HardwareDecodeCodecs = decodeCodecs,
            HardwareEncodeCodecs = encodeCodecs,
            AcceptsGpuSurfaceInput = true,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "Awaiting Commit 06 GPU export proof test."
        };

        return ValueTask.FromResult(report);
    }
}
