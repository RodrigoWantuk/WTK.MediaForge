namespace WTK.MediaForge.Core.Media;

public sealed record MediaForgeHardwareAdapterInfo
{
    public required string Platform { get; init; }

    public required string AdapterId { get; init; }

    public required string DeviceName { get; init; }

    public string? Vendor { get; init; }

    public string? DriverVersion { get; init; }

    public long DeviceGeneration { get; init; }
}

public sealed record MediaForgeCapabilitySnapshot
{
    public required long Generation { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required MediaForgeHardwareAdapterInfo Adapter { get; init; }

    public required MediaForgeCapabilityReport Report { get; init; }
}
