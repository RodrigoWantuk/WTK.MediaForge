namespace WTK.MediaForge.Core.Media;

public sealed class GpuVideoFrameDescriptor
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string Format { get; init; }

    public MediaTransportKind TransportKind { get; init; } = MediaTransportKind.GpuSurface;
}
