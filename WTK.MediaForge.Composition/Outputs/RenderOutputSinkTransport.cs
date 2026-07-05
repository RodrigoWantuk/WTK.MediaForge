using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

public static class RenderOutputSinkTransport
{
    public static MediaTransportKind GetAcceptedTransport(RenderOutputSinkKind kind) => kind switch
    {
        RenderOutputSinkKind.FrameNotification => MediaTransportKind.GpuSurface,
        RenderOutputSinkKind.CpuReadback => MediaTransportKind.DebugOnlyCpuReadback,
        RenderOutputSinkKind.Preview => MediaTransportKind.GpuSurface,
        RenderOutputSinkKind.Encoder => MediaTransportKind.EncodedPacket,
        RenderOutputSinkKind.Streaming => MediaTransportKind.EncodedPacket,
        RenderOutputSinkKind.File => MediaTransportKind.EncodedPacket,
        RenderOutputSinkKind.Ndi => MediaTransportKind.GpuSurface,
        RenderOutputSinkKind.Custom => MediaTransportKind.GpuSurface,
        _ => MediaTransportKind.GpuSurface
    };

    public static bool IsProductSink(RenderOutputSinkKind kind) => kind switch
    {
        RenderOutputSinkKind.Preview => true,
        RenderOutputSinkKind.Encoder => true,
        RenderOutputSinkKind.Streaming => true,
        RenderOutputSinkKind.File => true,
        _ => false
    };
}
