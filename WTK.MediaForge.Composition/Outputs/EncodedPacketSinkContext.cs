using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class EncodedPacketSinkContext
{
    public required EncodedVideoCodec Codec { get; init; }

    public required FrameSize Size { get; init; }

    public double FramesPerSecond { get; init; } = 60;
}
