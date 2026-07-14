using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class EncodedVideoProfile
{
    public EncodedVideoCodec Codec { get; init; } = EncodedVideoCodec.H264;

    public int FramesPerSecond { get; init; } = 60;

    public int BitrateBitsPerSecond { get; init; } = 8_000_000;

    public int KeyFrameIntervalFrames { get; init; } = 120;

    public string PixelFormat { get; init; } = "NV12";

    public string H264Profile { get; init; } = "High";

    public string H264Level { get; init; } = "4.2";

    public static EncodedVideoProfile DefaultH264 { get; } = new();
}
