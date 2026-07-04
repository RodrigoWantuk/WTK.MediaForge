using WTK.MediaForge.Core.Identifiers;

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

    private static IEnumerable<MediaSourceTypeDescriptor> CreateDescriptors()
    {
        yield return Live("Desktop capture", MediaSourceTypes.Desktop, hasAudio: false);
        yield return Live("Webcam", MediaSourceTypes.Webcam, hasAudio: true);
        yield return Live("NDI input", MediaSourceTypes.NdiInput, hasAudio: true);
        yield return Live("RTSP stream", MediaSourceTypes.RtspInput, hasAudio: true);
        yield return Live("IP camera", MediaSourceTypes.IpCamera, hasAudio: true);
        yield return File("Video file", MediaSourceTypes.VideoFile, hasAudio: true);
        yield return File("Image file", MediaSourceTypes.ImageFile, hasAudio: false);
        yield return File("Animated image", MediaSourceTypes.AnimatedImage, hasAudio: false);
        yield return File("Lottie animation", MediaSourceTypes.Lottie, hasAudio: false);
        yield return Live("Window capture", MediaSourceTypes.WindowCapture, hasAudio: false);
        yield return new MediaSourceTypeDescriptor
        {
            TypeId = MediaSourceTypes.Generated,
            DisplayName = "Generated",
            IsLive = false,
            HasVideo = true,
            HasAudio = false,
            RequiresGpuInterop = true
        };
    }

    private static MediaSourceTypeDescriptor Live(string displayName, MediaSourceTypeId typeId, bool hasAudio) =>
        new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            IsLive = true,
            HasVideo = true,
            HasAudio = hasAudio,
            RequiresGpuInterop = true
        };

    private static MediaSourceTypeDescriptor File(string displayName, MediaSourceTypeId typeId, bool hasAudio) =>
        new()
        {
            TypeId = typeId,
            DisplayName = displayName,
            IsLive = false,
            HasVideo = true,
            HasAudio = hasAudio,
            RequiresGpuInterop = true
        };
}
