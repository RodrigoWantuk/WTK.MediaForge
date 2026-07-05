using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Linux.Media;

public sealed class LinuxHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var report = new HardwareMediaCapabilityReport
        {
            Platform = "Linux",
            DetectedApis = ["Vulkan", "VAAPI-Planned"],
            HardwareDecodeCodecs = ["H264-VAAPI-Planned"],
            HardwareEncodeCodecs = ["H264-VAAPI-Planned"],
            AcceptsGpuSurfaceInput = true,
            RequiresCpuStaging = false,
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "Linux GPU media skeleton; VAAPI boundary only."
        };

        return ValueTask.FromResult(report);
    }
}
