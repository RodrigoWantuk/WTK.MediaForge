using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Mac.Media;

public sealed class MacHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var report = new HardwareMediaCapabilityReport
        {
            Platform = "macOS",
            DetectedApis = ["Vulkan-Planned", "Metal", "VideoToolbox-Planned"],
            HardwareDecodeCodecs = ["H264-VideoToolbox-Planned"],
            HardwareEncodeCodecs = ["H264-VideoToolbox-Planned"],
            AcceptsGpuSurfaceInput = true,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "macOS GPU media skeleton; VideoToolbox boundary only."
        };

        return ValueTask.FromResult(report);
    }
}
