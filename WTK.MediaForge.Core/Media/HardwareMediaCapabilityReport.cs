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

    public IReadOnlyList<HardwareMediaBackendCapability> BackendCapabilities { get; init; } =
        Array.Empty<HardwareMediaBackendCapability>();

    public bool AcceptsGpuSurfaceInput { get; init; }

    public bool RequiresCpuStaging { get; init; }

    public GpuExportProofStatus ExportProofStatus { get; init; } = GpuExportProofStatus.Pending;

    public string? ExportProofReason { get; init; }
}

public sealed class HardwareMediaBackendCapability
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Platform { get; init; }

    public string? Vendor { get; init; }

    public IReadOnlyList<string> DecodeCodecs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> EncodeCodecs { get; init; } = Array.Empty<string>();

    public bool RequiresGpuSurface { get; init; } = true;

    public bool RequiresCpuStaging { get; init; }

    public required MediaForgeSupportStatus SupportStatus { get; init; }

    public required MediaForgeProductReadinessStatus ProductReadinessStatus { get; init; }

    public string? UnavailableReason { get; init; }
}

public enum GpuExportProofStatus
{
    Pending,
    Passed,
    Failed
}
