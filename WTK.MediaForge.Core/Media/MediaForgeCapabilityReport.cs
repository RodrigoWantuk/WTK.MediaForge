namespace WTK.MediaForge.Core.Media;

public sealed class MediaForgeCapabilityReport
{
    public required HardwareMediaCapabilityReport Hardware { get; init; }

    public required IReadOnlyList<CapabilityEntry> Entries { get; init; }

    public CapabilityEntry? TryGetEntry(string id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public bool IsFeatureAvailable(string id) =>
        TryGetEntry(id)?.SupportStatus is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental;
}
