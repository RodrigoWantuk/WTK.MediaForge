using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Outputs.Settings;

public sealed class EncodedVideoProfile
{
    public EncodedVideoCodec Codec { get; init; } = EncodedVideoCodec.H264;

    public int FramesPerSecond { get; init; } = 60;

    public int BitrateBitsPerSecond { get; init; } = 8_000_000;

    public int KeyFrameIntervalFrames { get; init; } = 120;

    public string PixelFormat { get; init; } = "NV12";

    public H264Profile H264Profile { get; init; } = H264Profile.High;

    public H264Level H264Level { get; init; } = H264Level.Level42;

    public static EncodedVideoProfile DefaultH264 { get; } = new();
}
