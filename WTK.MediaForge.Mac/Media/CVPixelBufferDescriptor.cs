namespace WTK.MediaForge.Mac.Media;

public sealed class CVPixelBufferDescriptor
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required uint PixelFormat { get; init; }

    public nint Handle { get; init; }
}
