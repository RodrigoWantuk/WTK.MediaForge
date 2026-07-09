namespace WTK.MediaForge.Core.Media;

public static class MediaForgeCapabilityReportBuilder
{
    public static MediaForgeCapabilityReport Build(
        HardwareMediaCapabilityReport hardware,
        IEnumerable<CapabilityEntry>? additionalEntries = null)
    {
        var entries = new List<CapabilityEntry>(MediaForgeCapabilityCatalog.CreateDefaultEntries(hardware.ExportProofStatus));
        if (additionalEntries is not null)
            entries.AddRange(additionalEntries);

        EnsureUniqueCapabilityIds(entries);

        return new MediaForgeCapabilityReport
        {
            Hardware = hardware,
            Entries = entries
        };
    }

    private static void EnsureUniqueCapabilityIds(IReadOnlyList<CapabilityEntry> entries)
    {
        var duplicate = entries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            throw new InvalidOperationException($"Capability report contains duplicate capability id '{duplicate.Key}'.");
    }
}
