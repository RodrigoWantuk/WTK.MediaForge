namespace WTK.MediaForge.Core.Media.Encode;

public sealed class HardwareVideoEncoderSettings
{
    public EncodedVideoCodec Codec { get; init; } = EncodedVideoCodec.H264;

    public required int Width { get; init; }

    public required int Height { get; init; }

    public int FramesPerSecond { get; init; } = 60;

    public int BitrateBitsPerSecond { get; init; } = 8_000_000;

    public int KeyFrameIntervalFrames { get; init; } = 120;

    public string PixelFormat { get; init; } = "NV12";

    public string H264Profile { get; init; } = "High";

    public string H264Level { get; init; } = "4.2";

    public int MaxPendingInputSurfaces { get; init; } = 32;

    public void Validate()
    {
        if (Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"Only H.264 hardware encoding is implemented, not '{Codec}'.");

        if (Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "Encoder width must be positive.");

        if (Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(Height), "Encoder height must be positive.");

        if (FramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "Encoder frame rate must be positive.");

        if (BitrateBitsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(BitrateBitsPerSecond), "Encoder bitrate must be positive.");

        if (KeyFrameIntervalFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(KeyFrameIntervalFrames), "Encoder keyframe interval must be positive.");

        if (MaxPendingInputSurfaces <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingInputSurfaces),
                "Encoder pending input surface limit must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(PixelFormat);
        _ = GetH264ProfileValue(H264Profile);
        _ = GetH264LevelValue(H264Level);
    }

    public static uint GetH264ProfileValue(string profile) =>
        profile.Trim().ToUpperInvariant() switch
        {
            "BASELINE" => 66,
            "MAIN" => 77,
            "HIGH" => 100,
            _ => throw new ArgumentOutOfRangeException(
                nameof(profile), profile, "H.264 profile must be Baseline, Main, or High.")
        };

    public static uint GetH264LevelValue(string level) =>
        level.Trim() switch
        {
            "3.0" => 30,
            "3.1" => 31,
            "3.2" => 32,
            "4.0" => 40,
            "4.1" => 41,
            "4.2" => 42,
            "5.0" => 50,
            "5.1" => 51,
            "5.2" => 52,
            _ => throw new ArgumentOutOfRangeException(
                nameof(level), level, "H.264 level must be one of 3.0-3.2, 4.0-4.2, or 5.0-5.2.")
        };
}
