namespace WTK.MediaForge.Linux.Media;

public sealed class DmaBufImportDescriptor
{
    public required int FileDescriptor { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required uint Format { get; init; }

    public required ulong Modifier { get; init; }
}
