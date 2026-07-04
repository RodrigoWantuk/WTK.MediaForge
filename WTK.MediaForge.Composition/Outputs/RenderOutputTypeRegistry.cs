using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public static class RenderOutputTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, RenderOutputTypeDescriptor> Descriptors =
        CreateDescriptors().ToDictionary(d => d.TypeId.Value, StringComparer.Ordinal);

    public static IReadOnlyList<RenderOutputTypeDescriptor> All { get; } = Descriptors.Values.ToList();

    public static bool IsKnown(RenderOutputTypeId typeId) =>
        !typeId.IsEmpty && Descriptors.ContainsKey(typeId.Value);

    public static bool TryGetDescriptor(RenderOutputTypeId typeId, out RenderOutputTypeDescriptor? descriptor) =>
        Descriptors.TryGetValue(typeId.Value, out descriptor);

    private static IEnumerable<RenderOutputTypeDescriptor> CreateDescriptors()
    {
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.PreviewWindow,
            DisplayName = "Preview window",
            RequiresWindowHandle = true,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.Offscreen,
            DisplayName = "Offscreen",
            RequiresWindowHandle = false,
            IsHeadless = true
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.Ndi,
            DisplayName = "NDI output",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.EncodedFile,
            DisplayName = "Encoded file",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.RecordingMp4,
            DisplayName = "MP4 recording",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.StreamingRtmp,
            DisplayName = "RTMP streaming",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.StreamingSrt,
            DisplayName = "SRT streaming",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.StreamingRtsp,
            DisplayName = "RTSP streaming",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.StreamingHls,
            DisplayName = "HLS streaming",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
        yield return new RenderOutputTypeDescriptor
        {
            TypeId = RenderOutputTypes.VirtualCamera,
            DisplayName = "Virtual camera",
            RequiresWindowHandle = false,
            IsHeadless = false
        };
    }
}
