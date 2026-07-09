using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Sources;

public interface IStaticImageAssetDecoder
{
    StaticCpuAsset Decode(string path);
}

public static class StaticImageAssetFormats
{
    public static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StaticCpuAsset
{
    public required string Path { get; init; }

    public required FrameSize Size { get; init; }

    public required RenderPixelFormat PixelFormat { get; init; }

    public required byte[] Pixels { get; init; }

    public required MediaTransportKind TransportKind { get; init; }
}
