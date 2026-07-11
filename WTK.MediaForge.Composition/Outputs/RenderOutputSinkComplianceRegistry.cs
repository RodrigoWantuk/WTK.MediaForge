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
            UnavailableReason = "Experimental until the PreviewPanelSink local reliability gate is complete."
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
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly,
            UnavailableReason = "Prototype only until real hardware encoder output and production MP4 muxing are validated."
        },
        new()
        {
            SinkTypeName = nameof(RtmpPacketSink),
            Kind = RenderOutputSinkKind.Streaming,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly,
            UnavailableReason = "Prototype only until a real network RTMP transport is implemented and validated."
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
            UnavailableReason = "Unsupported until NDI SDK licensing and a GPU-safe path are approved."
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
