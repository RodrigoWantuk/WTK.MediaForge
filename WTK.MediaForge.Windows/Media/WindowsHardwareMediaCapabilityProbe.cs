using System.Runtime.InteropServices;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Windows.Media;

public sealed class WindowsHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var apis = new List<string> { "D3D11", "Vulkan", "MediaFoundation" };
        var report = new HardwareMediaCapabilityReport
        {
            Platform = RuntimeInformation.OSDescription,
            GpuVendor = "Unknown",
            DeviceName = "Unknown",
            DetectedApis = apis,
            HardwareDecodeCodecs = [],
            HardwareEncodeCodecs = [],
            AcceptsGpuSurfaceInput = false,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "Real Media Foundation hardware encode/decode output validation has not completed; prototype bridges are excluded."
        };

        return ValueTask.FromResult(report);
    }
}
