using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Runtime;

public sealed class EncodedRouteCompatibilityKeyTests
{
    [Fact]
    public void Mp4_and_rtmp_with_same_pixels_and_profile_share_render_and_encoder_keys()
    {
        var canvasId = CanvasId.New();
        var profile = new EncodedVideoProfile { BitrateBitsPerSecond = 6_000_000 };
        var recording = CreateOutput(
            RenderOutputTypes.RecordingMp4,
            RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("program.mp4", profile)),
            canvasId);
        var streaming = CreateOutput(
            RenderOutputTypes.StreamingRtmp,
            RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp("rtmp://localhost/live", "stream", profile)),
            canvasId);

        var recordingKey = EncodedRouteCompatibilityKey.Create(recording);
        var streamingKey = EncodedRouteCompatibilityKey.Create(streaming);

        Assert.Equal(recordingKey.RenderedOutput, streamingKey.RenderedOutput);
        Assert.Equal(recordingKey.Encoder, streamingKey.Encoder);
        Assert.NotEqual(SinkCompatibilityKey.Create(recording), SinkCompatibilityKey.Create(streaming));
    }

    [Fact]
    public void Different_bitrate_reuses_rendered_pixels_but_not_encoder()
    {
        var canvasId = CanvasId.New();
        var first = CreateOutput(
            RenderOutputTypes.RecordingMp4,
            RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4(
                "first.mp4",
                new EncodedVideoProfile { BitrateBitsPerSecond = 4_000_000 })),
            canvasId);
        var second = CreateOutput(
            RenderOutputTypes.RecordingMp4,
            RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4(
                "second.mp4",
                new EncodedVideoProfile { BitrateBitsPerSecond = 8_000_000 })),
            canvasId);

        var firstKey = EncodedRouteCompatibilityKey.Create(first);
        var secondKey = EncodedRouteCompatibilityKey.Create(second);

        Assert.Equal(firstKey.RenderedOutput, secondKey.RenderedOutput);
        Assert.NotEqual(firstKey.Encoder, secondKey.Encoder);
    }

    private static MediaForgeRenderOutput CreateOutput(
        RenderOutputTypeId typeId,
        System.Text.Json.Nodes.JsonObject settings,
        CanvasId canvasId) =>
        new()
        {
            Id = RenderOutputId.New(),
            Name = typeId.Value,
            TypeId = typeId,
            CanvasId = canvasId,
            OutputSize = new FrameSize(1920, 1080),
            Settings = settings
        };
}
