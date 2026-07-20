using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class RenderOutputSinkComplianceEntry
{
    public required string SinkTypeName { get; init; }

    public required RenderOutputSinkKind Kind { get; init; }

    public required MediaTransportKind Transport { get; init; }

    public required bool IsProductSink { get; init; }

    public required MediaForgeSupportStatus SupportStatus { get; init; }

    public string? UnavailableReason { get; init; }
}

public static class RenderOutputSinkComplianceRegistry
{
    private static readonly IReadOnlyList<RenderOutputSinkComplianceEntry> Entries =
    [
        new()
        {
            SinkTypeName = nameof(PreviewPanelSink),
            Kind = RenderOutputSinkKind.Preview,
            Transport = MediaTransportKind.GpuSurface,
            IsProductSink = true,
            SupportStatus = MediaForgeSupportStatus.Experimental,
            UnavailableReason = "Experimental until hosted resize and repeated fence-timeout recovery complete the preview reliability gate."
        },
        new()
        {
            SinkTypeName = nameof(CpuReadbackSink),
            Kind = RenderOutputSinkKind.CpuReadback,
            Transport = MediaTransportKind.DebugOnlyCpuReadback,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Supported,
            UnavailableReason = "Debug/sample/validation only; not a product preview, recording, or streaming sink."
        },
        new()
        {
            SinkTypeName = nameof(RecordingMp4PacketSink),
            Kind = RenderOutputSinkKind.File,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = true,
            SupportStatus = MediaForgeSupportStatus.Supported,
            UnavailableReason = null
        },
        new()
        {
            SinkTypeName = nameof(RtmpPacketSink),
            Kind = RenderOutputSinkKind.Streaming,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = true,
            SupportStatus = MediaForgeSupportStatus.Supported,
            UnavailableReason = null
        },
        new()
        {
            SinkTypeName = "SrtSink",
            Kind = RenderOutputSinkKind.Streaming,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Planned,
            UnavailableReason = "Planned after license and transport design review."
        },
        new()
        {
            SinkTypeName = "NdiSink",
            Kind = RenderOutputSinkKind.Ndi,
            Transport = MediaTransportKind.GpuSurface,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Unsupported,
            UnavailableReason = "Unsupported until a GPU-safe NDI video output path is validated. Standard SDK discovery/runtime redistribution does not satisfy rendered surface transport."
        },
        new()
        {
            SinkTypeName = "VirtualCameraSink",
            Kind = RenderOutputSinkKind.Custom,
            Transport = MediaTransportKind.GpuSurface,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Unsupported,
            UnavailableReason = "Unsupported until a platform virtual camera path is designed and validated."
        }
    ];

    public static IReadOnlyList<RenderOutputSinkComplianceEntry> All => Entries;

    public static bool IsCompliantProductSink(Type sinkType)
    {
        ArgumentNullException.ThrowIfNull(sinkType);

        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.SinkTypeName, sinkType.Name, StringComparison.Ordinal));

        if (entry is null)
            return false;

        return entry.IsProductSink &&
               entry.Transport is MediaTransportKind.GpuSurface or MediaTransportKind.EncodedPacket;
    }

    public static bool AcceptsRawCpuFrames(RenderOutputSinkKind kind) =>
        RenderOutputSinkTransport.GetAcceptedTransport(kind) ==
        MediaTransportKind.DebugOnlyCpuReadback;
}
