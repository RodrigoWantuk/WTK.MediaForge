namespace WTK.MediaForge.Core.Media;

public sealed class CapabilityEntry
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string DisplayName { get; init; }

    public required MediaForgeSupportStatus SupportStatus { get; init; }

    public required MediaForgeLicenseStatus LicenseStatus { get; init; }

    public string? UnavailableReason { get; init; }

    public MediaTransportKind? TransportKind { get; init; }
}
