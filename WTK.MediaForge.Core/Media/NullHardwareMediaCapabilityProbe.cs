namespace WTK.MediaForge.Core.Media;

public sealed class NullHardwareMediaCapabilityProbe : IHardwareMediaCapabilityProbe
{
    public ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new HardwareMediaCapabilityReport
        {
            Platform = "Unknown",
            ExportProofStatus = GpuExportProofStatus.Pending,
            ExportProofReason = "No platform probe registered."
        });
}
