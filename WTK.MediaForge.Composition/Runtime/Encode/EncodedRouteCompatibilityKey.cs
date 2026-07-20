using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal readonly record struct EncodedRouteCompatibilityKey(
    CanvasId CanvasId,
    FrameSize OutputSize,
    LayoutMode LayoutMode,
    ColorRgba LetterboxColor,
    RenderColorSpace ColorSpace,
    EncodedVideoCodec Codec,
    int FramesPerSecond,
    int BitrateBitsPerSecond,
    int KeyFrameIntervalFrames,
    string PixelFormat,
    string H264Profile,
    string H264Level)
{
    public static EncodedRouteCompatibilityKey Create(MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var profile = output.TypeId switch
        {
            var typeId when typeId == RenderOutputTypes.RecordingMp4 =>
                ((RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Video,
            var typeId when typeId == RenderOutputTypes.StreamingRtmp =>
                ((StreamingRtmpOutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Video,
            _ => throw new ArgumentException(
                $"Output type '{output.TypeId.Value}' is not an H.264 encoded output.",
                nameof(output))
        };

        return new EncodedRouteCompatibilityKey(
            output.CanvasId,
            output.OutputSize,
            output.CanvasLayoutMode,
            output.LetterboxColor,
            output.ColorSpace,
            profile.Codec,
            profile.FramesPerSecond,
            profile.BitrateBitsPerSecond,
            profile.KeyFrameIntervalFrames,
            profile.PixelFormat.ToUpperInvariant(),
            profile.H264Profile.ToUpperInvariant(),
            profile.H264Level.ToUpperInvariant());
    }
}

internal sealed record EncodedOutputSinkRegistration(
    RenderOutputId OutputId,
    IEncodedPacketSink Sink,
    EncodedOutputBackpressurePolicy Policy);
