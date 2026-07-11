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
        EnsureUnavailableEntriesHaveReasons(entries);
        EnsureHardwareBackendsDoNotOverstateAvailability(hardware.BackendCapabilities);

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

    private static void EnsureUnavailableEntriesHaveReasons(IReadOnlyList<CapabilityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (IsUserAvailable(entry.SupportStatus))
                continue;

            if (string.IsNullOrWhiteSpace(entry.UnavailableReason))
            {
                throw new InvalidOperationException(
                    $"Capability '{entry.Id}' is marked {entry.SupportStatus} but does not provide an unavailable reason.");
            }
        }
    }

    private static void EnsureHardwareBackendsDoNotOverstateAvailability(
        IReadOnlyList<HardwareMediaBackendCapability> backendCapabilities)
    {
        foreach (var backend in backendCapabilities)
        {
            if (backend.RequiresCpuStaging && IsUserAvailable(backend.SupportStatus))
            {
                throw new InvalidOperationException(
                    $"Hardware media backend '{backend.Id}' is marked {backend.SupportStatus} but requires CPU staging.");
            }

            if (IsUserAvailable(backend.SupportStatus) &&
                backend.ProductReadinessStatus is
                    MediaForgeProductReadinessStatus.Prototype or
                    MediaForgeProductReadinessStatus.Skeleton)
            {
                throw new InvalidOperationException(
                    $"Hardware media backend '{backend.Id}' is marked {backend.SupportStatus} but readiness is {backend.ProductReadinessStatus}.");
            }

            if (!IsUserAvailable(backend.SupportStatus) &&
                string.IsNullOrWhiteSpace(backend.UnavailableReason))
            {
                throw new InvalidOperationException(
                    $"Hardware media backend '{backend.Id}' is marked {backend.SupportStatus} but does not provide an unavailable reason.");
            }
        }
    }

    private static bool IsUserAvailable(MediaForgeSupportStatus status) =>
        status is MediaForgeSupportStatus.Supported or MediaForgeSupportStatus.Experimental;
}
