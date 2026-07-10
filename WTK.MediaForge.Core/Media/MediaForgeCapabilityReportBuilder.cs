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
        EnsureReadinessDoesNotOverstateProductAvailability(entries);

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

    private static void EnsureReadinessDoesNotOverstateProductAvailability(IReadOnlyList<CapabilityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!IsUserAvailable(entry.SupportStatus))
                continue;

            if (entry.ProductReadinessStatus is
                MediaForgeProductReadinessStatus.Prototype or
                MediaForgeProductReadinessStatus.Skeleton)
            {
                throw new InvalidOperationException(
                    $"Capability '{entry.Id}' is marked {entry.SupportStatus} but readiness is {entry.ProductReadinessStatus}.");
            }
        }
    }

    private static bool IsUserAvailable(MediaForgeSupportStatus status) =>
        status is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental;
}
