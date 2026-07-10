using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class RenderOutputSinkComplianceEntry
{
    public required string SinkTypeName { get; init; }

    public required RenderOutputSinkKind Kind { get; init; }

    public required MediaTransportKind Transport { get; init; }

    public required bool IsProductSink { get; init; }

    public required MediaForgeSupportStatus SupportStatus { get; init; }
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
            SupportStatus = MediaForgeSupportStatus.Experimental
        },
        new()
        {
            SinkTypeName = nameof(CpuReadbackSink),
            Kind = RenderOutputSinkKind.CpuReadback,
            Transport = MediaTransportKind.DebugOnlyCpuReadback,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Supported
        },
        new()
        {
            SinkTypeName = nameof(RecordingMp4PacketSink),
            Kind = RenderOutputSinkKind.File,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly
        },
        new()
        {
            SinkTypeName = nameof(RtmpPacketSink),
            Kind = RenderOutputSinkKind.Streaming,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.PrototypeOnly
        },
        new()
        {
            SinkTypeName = "SrtSink",
            Kind = RenderOutputSinkKind.Streaming,
            Transport = MediaTransportKind.EncodedPacket,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Planned
        },
        new()
        {
            SinkTypeName = "NdiSink",
            Kind = RenderOutputSinkKind.Ndi,
            Transport = MediaTransportKind.GpuSurface,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Unsupported
        },
        new()
        {
            SinkTypeName = "VirtualCameraSink",
            Kind = RenderOutputSinkKind.Custom,
            Transport = MediaTransportKind.GpuSurface,
            IsProductSink = false,
            SupportStatus = MediaForgeSupportStatus.Unsupported
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
