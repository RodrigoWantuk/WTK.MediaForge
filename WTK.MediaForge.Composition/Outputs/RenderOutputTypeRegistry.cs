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
                "Experimental until hosted resize and repeated fence-timeout recovery complete the preview reliability gate.");
        }

        if (typeId == RenderOutputTypes.RecordingMp4)
        {
            return (
                MediaForgeSupportStatus.Unavailable,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.EncodedPacket,
                "Unavailable until the composite recording capability reports hardware encode, render-to-encode, MP4 recording, and MP4 output product proofs passed on this runtime.");
        }

        if (typeId == RenderOutputTypes.StreamingRtmp)
        {
            return (
                MediaForgeSupportStatus.Unavailable,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.EncodedPacket,
                "Unavailable until the composite RTMP capability reports hardware encode, render-to-encode, and RTMP network proofs passed on this runtime.");
        }

        if (typeId == RenderOutputTypes.RemoteScene)
        {
            return (
                MediaForgeSupportStatus.Planned,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.EncodedPacket,
                "Contract complete; unavailable until the native libwebrtc GPU media route passes Direct and TURN proofs.");
        }

        if (typeId == RenderOutputTypes.Ndi)
        {
            return (
                MediaForgeSupportStatus.Unsupported,
                MediaForgeProductReadinessStatus.Contract,
                MediaTransportKind.GpuSurface,
                "Blocked until a GPU-safe NDI video output path is validated. Standard SDK discovery/runtime redistribution is handled by the Windows adapter.");
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
                "Planned after MP4 recording is sustained; encoded file outputs must consume hardware-encoded packets only.");
        }

        return (
            MediaForgeSupportStatus.Planned,
            MediaForgeProductReadinessStatus.Contract,
            MediaTransportKind.GpuSurface,
            "Planned until a GPU-safe platform output path is implemented and validated.");
    }

    private static MediaForgeLicenseStatus ResolveLicenseStatus(RenderOutputTypeId typeId) =>
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
            TypeId = RenderOutputTypes.RemoteScene,
            DisplayName = "Remote Scene",
            RequiresWindowHandle = false,
            IsHeadless = true
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
