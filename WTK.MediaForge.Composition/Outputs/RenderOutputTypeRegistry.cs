using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

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

    public static IReadOnlyList<CapabilityEntry> CreateCapabilityEntries() =>
        All.Select(CreateCapabilityEntry).ToList();

    private static CapabilityEntry CreateCapabilityEntry(RenderOutputTypeDescriptor descriptor)
    {
        var (status, readiness, transport, reason) = ResolveCapability(descriptor.TypeId);
        return new CapabilityEntry
        {
            Id = $"output.{descriptor.TypeId.Value}",
            Category = CapabilityCategories.Sink,
            DisplayName = descriptor.DisplayName,
            SupportStatus = status,
            LicenseStatus = ResolveLicenseStatus(descriptor.TypeId),
            ProductReadinessStatus = readiness,
            TransportKind = transport,
            UnavailableReason = reason
        };
    }

    private static (
        MediaForgeSupportStatus Status,
        MediaForgeProductReadinessStatus Readiness,
        MediaTransportKind Transport,
        string? Reason) ResolveCapability(RenderOutputTypeId typeId)
    {
        if (typeId == RenderOutputTypes.Offscreen)
            return (MediaForgeSupportStatus.Supported, MediaForgeProductReadinessStatus.ProductValidated, MediaTransportKind.GpuSurface, null);

        if (typeId == RenderOutputTypes.PreviewWindow)
        {
            return (
                MediaForgeSupportStatus.Experimental,
                MediaForgeProductReadinessStatus.BackendCallSucceeded,
                MediaTransportKind.GpuSurface,
                "Experimental until the PreviewPanelSink local reliability gate is complete.");
        }

        if (typeId == RenderOutputTypes.RecordingMp4)
        {
            return (
                MediaForgeSupportStatus.PrototypeOnly,
                MediaForgeProductReadinessStatus.Prototype,
                MediaTransportKind.EncodedPacket,
                "Prototype only until hardware encode and MP4 output product proofs pass.");
        }

        if (typeId == RenderOutputTypes.StreamingRtmp)
        {
            return (
                MediaForgeSupportStatus.PrototypeOnly,
                MediaForgeProductReadinessStatus.Prototype,
                MediaTransportKind.EncodedPacket,
                "Prototype only until a real network RTMP transport is implemented and validated.");
        }

        if (typeId == RenderOutputTypes.Ndi)
        {
            return (
                MediaForgeSupportStatus.Unsupported,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.GpuSurface,
                "Unsupported until NDI SDK licensing and a GPU-safe output path are approved.");
        }

        if (typeId == RenderOutputTypes.StreamingSrt)
        {
            return (
                MediaForgeSupportStatus.Planned,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.EncodedPacket,
                "Planned after license and transport design review.");
        }

        if (typeId == RenderOutputTypes.EncodedFile)
        {
            return (
                MediaForgeSupportStatus.Planned,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.EncodedPacket,
                "Planned after MP4 hardware recording product proof; must consume hardware-encoded packets only.");
        }

        return (
            MediaForgeSupportStatus.Planned,
            MediaForgeProductReadinessStatus.Contract,
            MediaTransportKind.GpuSurface,
            "Planned until a GPU-safe platform output path is implemented and validated.");
    }

    private static MediaForgeLicenseStatus ResolveLicenseStatus(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.Ndi ||
        typeId == RenderOutputTypes.StreamingSrt ||
        typeId == RenderOutputTypes.StreamingRtsp ||
        typeId == RenderOutputTypes.StreamingHls
            ? MediaForgeLicenseStatus.RequiresLegalReview
            : MediaForgeLicenseStatus.Approved;

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
