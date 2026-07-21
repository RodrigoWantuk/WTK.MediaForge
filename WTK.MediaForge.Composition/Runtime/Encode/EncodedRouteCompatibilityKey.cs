using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal readonly record struct RenderedOutputCompatibilityKey(
    CanvasId CanvasId,
    SceneVersionBinding SceneVersionBinding,
    OutputRouteTransitionKind RouteTransitionKind,
    string RouteTransitionId,
    int RouteTransitionDurationMs,
    RenderOutputId? IsolatedOutputId,
    FrameSize OutputSize,
    LayoutMode LayoutMode,
    ColorRgba LetterboxColor,
    RenderColorSpace ColorSpace)
{
    public static RenderedOutputCompatibilityKey Create(MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.SceneVersionBinding.Validate();
        ValidateRouteTransition(output);
        return new RenderedOutputCompatibilityKey(
            output.CanvasId,
            output.SceneVersionBinding,
            output.RouteTransition.Kind,
            output.RouteTransition.Id,
            output.RouteTransition.DurationMs,
            // Runtime progress is output-specific until a resolved transition identity is exported.
            output.RouteTransition.Kind == OutputRouteTransitionKind.Fade ? output.Id : null,
            output.OutputSize,
            output.CanvasLayoutMode,
            output.LetterboxColor,
            output.ColorSpace);
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

internal readonly record struct EncoderCompatibilityKey(
    EncodedVideoCodec Codec,
    int FramesPerSecond,
    int BitrateBitsPerSecond,
    int KeyFrameIntervalFrames,
    string PixelFormat,
    H264Profile H264Profile,
    H264Level H264Level)
{
    public static EncoderCompatibilityKey Create(MediaForgeRenderOutput output)
    {
        var profile = EncodedOutputProfileResolver.Get(output);
        return new EncoderCompatibilityKey(
            profile.Codec,
            profile.FramesPerSecond,
            profile.BitrateBitsPerSecond,
            profile.KeyFrameIntervalFrames,
            profile.PixelFormat.ToUpperInvariant(),
            profile.H264Profile,
            profile.H264Level);
    }
}

internal readonly record struct SinkCompatibilityKey(
    RenderOutputId OutputId,
    RenderOutputTypeId OutputTypeId,
    string DestinationFingerprint)
{
    public static SinkCompatibilityKey Create(MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var destination = output.TypeId switch
        {
            var typeId when typeId == RenderOutputTypes.RecordingMp4 =>
                ((RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Path,
            var typeId when typeId == RenderOutputTypes.StreamingRtmp =>
                CreateRtmpDestination(
                    (StreamingRtmpOutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)),
            var typeId when typeId == RenderOutputTypes.RemoteScene =>
                CreateRemoteSceneDestination(
                    (RemoteSceneOutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)),
            _ => throw new ArgumentException(
                $"Output type '{output.TypeId.Value}' is not an H.264 encoded output.",
                nameof(output))
        };

        return new SinkCompatibilityKey(output.Id, output.TypeId, Hash(destination));
    }

    private static string CreateRtmpDestination(StreamingRtmpOutputSettings settings) =>
        $"{settings.Url.TrimEnd('/')}\0{settings.StreamKey}";

    private static string CreateRemoteSceneDestination(RemoteSceneOutputSettings settings) =>
        $"{settings.Provider}\0{settings.SignalingEndpoint.TrimEnd('/')}\0{settings.StreamName}\0{settings.SessionPolicy}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal readonly record struct EncodedRouteCompatibilityKey(
    RenderedOutputCompatibilityKey RenderedOutput,
    EncoderCompatibilityKey Encoder)
{
    public static EncodedRouteCompatibilityKey Create(MediaForgeRenderOutput output) =>
        new(RenderedOutputCompatibilityKey.Create(output), EncoderCompatibilityKey.Create(output));
}

internal static class EncodedOutputProfileResolver
{
    public static EncodedVideoProfile Get(MediaForgeRenderOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.TypeId switch
        {
            var typeId when typeId == RenderOutputTypes.RecordingMp4 =>
                ((RecordingMp4OutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Video,
            var typeId when typeId == RenderOutputTypes.StreamingRtmp =>
                ((StreamingRtmpOutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Video,
            var typeId when typeId == RenderOutputTypes.RemoteScene =>
                ((RemoteSceneOutputSettings)RenderOutputSettingsSerializer.Deserialize(typeId, output.Settings)).Video,
            _ => throw new ArgumentException(
                $"Output type '{output.TypeId.Value}' is not an H.264 encoded output.",
                nameof(output))
        };
    }
}

internal sealed record EncodedOutputSinkRegistration(
    RenderOutputId OutputId,
    IEncodedPacketSink Sink,
    EncodedOutputBackpressurePolicy Policy);
