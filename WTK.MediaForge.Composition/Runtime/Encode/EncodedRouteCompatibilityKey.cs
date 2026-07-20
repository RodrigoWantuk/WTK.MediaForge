using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal readonly record struct EncodedRouteCompatibilityKey(
    CanvasId CanvasId,
    SceneVersionBinding SceneVersionBinding,
    OutputRouteTransitionKind RouteTransitionKind,
    string RouteTransitionId,
    int RouteTransitionDurationMs,
    RenderOutputId? IsolatedOutputId,
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
        output.SceneVersionBinding.Validate();
        ValidateRouteTransition(output);
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
            output.SceneVersionBinding,
            output.RouteTransition.Kind,
            output.RouteTransition.Id,
            output.RouteTransition.DurationMs,
            // Fade progress is runtime state per output. Until the physical graph exports a
            // shared transition key, sharing a fade would risk sending the wrong pixels.
            output.RouteTransition.Kind == OutputRouteTransitionKind.Fade ? output.Id : null,
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

    private static void ValidateRouteTransition(MediaForgeRenderOutput output)
    {
        if (string.IsNullOrWhiteSpace(output.RouteTransition.Id))
            throw new ArgumentException("Encoded output route transition requires a stable id.", nameof(output));

        switch (output.RouteTransition.Kind)
        {
            case OutputRouteTransitionKind.Cut when output.RouteTransition.DurationMs != 0:
                throw new ArgumentException("Cut output route transitions must have zero duration.", nameof(output));

            case OutputRouteTransitionKind.Fade when output.RouteTransition.DurationMs <= 0:
                throw new ArgumentException("Fade output route transitions require a positive duration.", nameof(output));

            case OutputRouteTransitionKind.Cut:
            case OutputRouteTransitionKind.Fade:
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(output),
                    output.RouteTransition.Kind,
                    "Unsupported output route transition kind.");
        }
    }
}

internal sealed record EncodedOutputSinkRegistration(
    RenderOutputId OutputId,
    IEncodedPacketSink Sink,
    EncodedOutputBackpressurePolicy Policy);
