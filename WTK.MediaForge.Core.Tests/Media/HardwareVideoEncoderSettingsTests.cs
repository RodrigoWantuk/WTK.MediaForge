using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;
using Xunit;

namespace WTK.MediaForge.Core.Tests.Media;

public sealed class HardwareVideoEncoderSettingsTests
{
    [Fact]
    public void Validate_accepts_reasonable_h264_settings()
    {
        var settings = new HardwareVideoEncoderSettings
        {
            Codec = EncodedVideoCodec.H264,
            Width = 1920,
            Height = 1080,
            FramesPerSecond = 60,
            BitrateBitsPerSecond = 12_000_000,
            KeyFrameIntervalFrames = 120,
            PixelFormat = "NV12"
        };

        settings.Validate();
    }

    [Theory]
    [InlineData(0, 1080, 60, 12_000_000, 120, "NV12")]
    [InlineData(1920, 0, 60, 12_000_000, 120, "NV12")]
    [InlineData(1920, 1080, 0, 12_000_000, 120, "NV12")]
    [InlineData(1920, 1080, 60, 0, 120, "NV12")]
    [InlineData(1920, 1080, 60, 12_000_000, 0, "NV12")]
    [InlineData(1920, 1080, 60, 12_000_000, 120, "")]
    public void Validate_rejects_invalid_settings(
        int width,
        int height,
        int framesPerSecond,
        int bitrate,
        int keyFrameInterval,
        string pixelFormat)
    {
        var settings = new HardwareVideoEncoderSettings
        {
            Codec = EncodedVideoCodec.H264,
            Width = width,
            Height = height,
            FramesPerSecond = framesPerSecond,
            BitrateBitsPerSecond = bitrate,
            KeyFrameIntervalFrames = keyFrameInterval,
            PixelFormat = pixelFormat
        };

        Assert.ThrowsAny<ArgumentException>(() => settings.Validate());
    }
}
