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

    public H264Profile H264Profile { get; init; } = H264Profile.High;

    public H264Level H264Level { get; init; } = H264Level.Level42;

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
        if (!Enum.IsDefined(H264Profile))
            throw new ArgumentOutOfRangeException(nameof(H264Profile), H264Profile, "Unsupported H.264 profile.");

        if (!Enum.IsDefined(H264Level))
            throw new ArgumentOutOfRangeException(nameof(H264Level), H264Level, "Unsupported H.264 level.");
    }
}
