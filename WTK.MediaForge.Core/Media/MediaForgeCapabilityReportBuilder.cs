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

        return new MediaForgeCapabilityReport
        {
            Hardware = hardware,
            Entries = entries
        };
    }
}
