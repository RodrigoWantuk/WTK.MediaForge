using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Windows.Media.Encode;

public sealed class MediaFoundationHardwareEncoderProbe
{
    public IReadOnlyList<HardwareEncoderInfo> Probe()
    {
        // This intentionally returns no product encoder until real MF MFT
        // enumeration and backend output validation are implemented.
        return Array.Empty<HardwareEncoderInfo>();
    }

    public IReadOnlyList<CapabilityEntry> CreateVendorPlannedEntries() =>
    [
        new CapabilityEntry
        {
            Category = CapabilityCategories.Encode,
            Id = MediaForgeCapabilityCatalog.NvencDirect,
            DisplayName = "NVENC direct",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Vendor SDK direct path; not implemented in the first native hardware encoder track.",
            TransportKind = MediaTransportKind.EncodedPacket
        },
        new CapabilityEntry
        {
            Category = CapabilityCategories.Encode,
            Id = MediaForgeCapabilityCatalog.QsvDirect,
            DisplayName = "Intel QSV direct",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Vendor SDK direct path; not implemented in the first native hardware encoder track.",
            TransportKind = MediaTransportKind.EncodedPacket
        },
        new CapabilityEntry
        {
            Category = CapabilityCategories.Encode,
            Id = MediaForgeCapabilityCatalog.AmfDirect,
            DisplayName = "AMD AMF direct",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            ProductReadinessStatus = MediaForgeProductReadinessStatus.Contract,
            UnavailableReason = "Vendor SDK direct path; not implemented in the first native hardware encoder track.",
            TransportKind = MediaTransportKind.EncodedPacket
        }
    ];
}
