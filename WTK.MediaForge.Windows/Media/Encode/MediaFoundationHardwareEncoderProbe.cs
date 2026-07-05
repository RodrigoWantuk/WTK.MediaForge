using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Windows.Media.Encode;

public sealed class MediaFoundationHardwareEncoderProbe
{
    public IReadOnlyList<HardwareEncoderInfo> Probe()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<HardwareEncoderInfo>();

        return
        [
            new HardwareEncoderInfo
            {
                Name = "Media Foundation H.264 Hardware MFT",
                Codec = EncodedVideoCodec.H264,
                Backend = "MediaFoundation-HardwareMft",
                AcceptsGpuSurfaceInput = true
            }
        ];
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
            UnavailableReason = "Vendor SDK direct path; not implemented in MVP.",
            TransportKind = MediaTransportKind.EncodedPacket
        },
        new CapabilityEntry
        {
            Category = CapabilityCategories.Encode,
            Id = MediaForgeCapabilityCatalog.QsvDirect,
            DisplayName = "Intel QSV direct",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            UnavailableReason = "Vendor SDK direct path; not implemented in MVP.",
            TransportKind = MediaTransportKind.EncodedPacket
        },
        new CapabilityEntry
        {
            Category = CapabilityCategories.Encode,
            Id = MediaForgeCapabilityCatalog.AmfDirect,
            DisplayName = "AMD AMF direct",
            SupportStatus = MediaForgeSupportStatus.Planned,
            LicenseStatus = MediaForgeLicenseStatus.RequiresLegalReview,
            UnavailableReason = "Vendor SDK direct path; not implemented in MVP.",
            TransportKind = MediaTransportKind.EncodedPacket
        }
    ];
}
