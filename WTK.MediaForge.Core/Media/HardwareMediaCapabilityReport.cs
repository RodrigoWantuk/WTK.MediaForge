namespace WTK.MediaForge.Core.Media;

public sealed class HardwareMediaCapabilityReport
{
    public required string Platform { get; init; }

    public string? GpuVendor { get; init; }

    public string? DeviceName { get; init; }

    public string? DriverVersion { get; init; }

    public IReadOnlyList<string> DetectedApis { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HardwareDecodeCodecs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HardwareEncodeCodecs { get; init; } = Array.Empty<string>();

    public bool AcceptsGpuSurfaceInput { get; init; }

    public bool RequiresCpuStaging { get; init; }

    public GpuExportProofStatus ExportProofStatus { get; init; } = GpuExportProofStatus.Pending;

    public string? ExportProofReason { get; init; }
}

public enum GpuExportProofStatus
{
    Pending,
    Passed,
    Failed
}
