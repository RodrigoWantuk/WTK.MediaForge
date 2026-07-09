using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Sources;

public static class MediaSourceTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, MediaSourceTypeDescriptor> Descriptors =
        CreateDescriptors().ToDictionary(d => d.TypeId.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, MediaSourceTypeId> LegacyToCanonical =
        new Dictionary<string, MediaSourceTypeId>(StringComparer.Ordinal)
        {
            [LegacyMediaSourceTypeIds.DesktopCapture.Value] = MediaSourceTypes.Desktop,
            [LegacyMediaSourceTypeIds.ImageFile.Value] = MediaSourceTypes.ImageFile,
            [LegacyMediaSourceTypeIds.VideoFile.Value] = MediaSourceTypes.VideoFile
        };

    public static IReadOnlyList<MediaSourceTypeDescriptor> All { get; } = CreateDescriptors().ToList();

    public static bool IsKnown(MediaSourceTypeId typeId)
    {
        if (typeId.IsEmpty)
            return false;

        return Descriptors.ContainsKey(typeId.Value) || LegacyToCanonical.ContainsKey(typeId.Value);
    }

    public static bool TryGetDescriptor(MediaSourceTypeId typeId, out MediaSourceTypeDescriptor? descriptor)
    {
        var canonical = ResolveCanonical(typeId);
        return Descriptors.TryGetValue(canonical.Value, out descriptor);
    }

    public static MediaSourceTypeId ResolveCanonical(MediaSourceTypeId typeId)
    {
        if (typeId.IsEmpty)
            return typeId;

        return LegacyToCanonical.TryGetValue(typeId.Value, out var canonical)
            ? canonical
            : typeId;
    }

    public static bool IsLegacy(MediaSourceTypeId typeId) =>
        LegacyToCanonical.ContainsKey(typeId.Value);

    public static IReadOnlyList<CapabilityEntry> CreateCapabilityEntries() =>
        All.Select(d => new CapabilityEntry
        {
            Id = $"source.{d.TypeId.Value}",
            Category = CapabilityCategories.Source,
            DisplayName = d.DisplayName,
            SupportStatus = d.SupportStatus,
            LicenseStatus = MediaForgeLicenseStatus.Approved,
            UnavailableReason = d.UnavailableReason,
            TransportKind = d.OutputTransport
        }).ToList();

    private static IEnumerable<MediaSourceTypeDescriptor> CreateDescriptors()
    {
        yield return LiveGpu("Desktop capture", MediaSourceTypes.Desktop, MediaForgeSupportStatus.Experimental);
        yield return LiveGpu("Window capture", MediaSourceTypes.WindowCapture, MediaForgeSupportStatus.Experimental);
        yield return new MediaSourceTypeDescriptor
        {
            TypeId = MediaSourceTypes.Webcam,
            DisplayName = "Webcam",
            Category = MediaSourceCategory.Live,
            OutputTransport = MediaTransportKind.GpuSurface,
            IsLive = true,
            IsTimeline = false,
            HasVideo = true,
            HasAudio = true,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = false,
            AllowsRawCpuException = true,
            RawCpuExceptionKind = RawCpuVideoFrameExceptionKind.WebcamSystemRawInput,
            SupportStatus = MediaForgeSupportStatus.Experimental
        };
        yield return new MediaSourceTypeDescriptor
        {
            TypeId = MediaSourceTypes.ImageFile,
            DisplayName = "Image file",
            Category = MediaSourceCategory.Static,
            OutputTransport = MediaTransportKind.StaticCpuAsset,
            IsLive = false,
            IsTimeline = false,
            HasVideo = true,
            HasAudio = false,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = false,
            AllowsRawCpuException = false,
            SupportStatus = MediaForgeSupportStatus.Supported
        };
        yield return TimelineEncoded(
            "Video file",
            MediaSourceTypes.VideoFile,
            hasAudio: true,
            MediaForgeSupportStatus.PrototypeOnly,
            "Prototype only: current file-video runtime does not yet demux/decode real media into GPU frames.");
        yield return TimelineEncoded("RTSP stream", MediaSourceTypes.RtspInput, hasAudio: true);
        yield return TimelineEncoded("IP camera", MediaSourceTypes.IpCamera, hasAudio: true);
        yield return Blocked("Animated image", MediaSourceTypes.AnimatedImage, "Blocked until GPU-safe frame strategy.");
        yield return Blocked("Lottie animation", MediaSourceTypes.Lottie, "Blocked until GPU-safe rasterization strategy.");
        yield return Blocked("NDI input", MediaSourceTypes.NdiInput, "Unsupported until license and GPU path.", MediaForgeSupportStatus.Unsupported);
        yield return new MediaSourceTypeDescriptor
        {
            TypeId = MediaSourceTypes.Generated,
            DisplayName = "Generated",
            Category = MediaSourceCategory.Generated,
            OutputTransport = MediaTransportKind.GpuSurface,
            IsLive = false,
            IsTimeline = false,
            HasVideo = true,
            HasAudio = false,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = false,
            AllowsRawCpuException = false,
            SupportStatus = MediaForgeSupportStatus.Experimental
        };
    }

    private static MediaSourceTypeDescriptor LiveGpu(string displayName, MediaSourceTypeId typeId, MediaForgeSupportStatus status) =>
        new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            Category = MediaSourceCategory.Live,
            OutputTransport = MediaTransportKind.GpuSurface,
            IsLive = true,
            IsTimeline = false,
            HasVideo = true,
            HasAudio = false,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = false,
            AllowsRawCpuException = false,
            SupportStatus = status
        };

    private static MediaSourceTypeDescriptor TimelineEncoded(
        string displayName,
        MediaSourceTypeId typeId,
        bool hasAudio,
        MediaForgeSupportStatus status = MediaForgeSupportStatus.Planned,
        string? unavailableReason = null) =>
        new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            Category = MediaSourceCategory.Timeline,
            OutputTransport = MediaTransportKind.EncodedPacket,
            IsLive = false,
            IsTimeline = true,
            HasVideo = true,
            HasAudio = hasAudio,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = true,
            AllowsRawCpuException = false,
            SupportStatus = status,
            UnavailableReason = unavailableReason ?? "Hardware decode required; software decode prohibited."
        };

    private static MediaSourceTypeDescriptor Blocked(
        string displayName,
        MediaSourceTypeId typeId,
        string reason,
        MediaForgeSupportStatus status = MediaForgeSupportStatus.Planned) =>
        new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            Category = MediaSourceCategory.Timeline,
            OutputTransport = MediaTransportKind.GpuSurface,
            IsLive = false,
            IsTimeline = true,
            HasVideo = true,
            HasAudio = false,
            RequiresGpuInterop = true,
            RequiresHardwareDecode = true,
            AllowsRawCpuException = false,
            SupportStatus = status,
            UnavailableReason = reason
        };
}
