using System.Numerics;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static partial class CompositionPushConstantsBuilder
{
    public static MediaForgeLayerPushConstants BuildSourceLayer(
        RenderSourceLayerDrawObjectSnapshot layer,
        GpuFrameReference frame,
        ChromaKeyEffectSnapshot? chromaKey,
        ColorCorrectionEffectSnapshot? colorCorrection)
    {
        var crop = layer.EffectiveCrop;
        var keyColor = chromaKey?.KeyColor ?? ColorRgba.Transparent;
        var letterbox = layer.LetterboxColor;
        var brightness = colorCorrection?.Brightness ?? 0f;
        var contrast = colorCorrection?.Contrast ?? 1f;
        var saturation = colorCorrection?.Saturation ?? 1f;
        var hueDegrees = colorCorrection?.HueDegrees ?? 0f;

        return new MediaForgeLayerPushConstants
        {
            CropRect = new Vector4(crop.Left, crop.Top, crop.Right, crop.Bottom),
            ChromaKeyColor = new Vector4(keyColor.R, keyColor.G, keyColor.B, brightness),
            ChromaKeyParameters = chromaKey is null
                ? new Vector4(-1f, 0f, 0f, hueDegrees)
                : new Vector4(
                    chromaKey.Similarity,
                    chromaKey.Smoothness,
                    chromaKey.SpillReduction,
                    hueDegrees),
            LogicalSize = new Vector2(frame.LogicalSize.Width, frame.LogicalSize.Height),
            BoxSize = new Vector2(layer.Transform.Size.Width, layer.Transform.Size.Height),
            Pivot = new Vector2(layer.Transform.Pivot.X, layer.Transform.Pivot.Y),
            Opacity = layer.Opacity,
            LayoutMode = (int)layer.LayoutMode,
            ContentRotation = layer.ContentRotationOverride is { } rotationOverride
                ? (int)rotationOverride
                : CapturePreviewGeometry.ResolveShaderRotation(
                    frame.Rotation,
                    frame.LogicalSize,
                    frame.TextureSize),
            RotationDegrees = layer.Transform.RotationDegrees,
            ColorContrast = contrast,
            ColorSaturation = saturation,
            LetterboxColor = ToVector4(letterbox)
        };
    }

    public static MediaForgeOutputPushConstants BuildOutputLetterbox(
        RenderOutputStateSnapshot output,
        FrameSize canvasSize)
    {
        var letterbox = output.LetterboxColor;
        return new MediaForgeOutputPushConstants
        {
            CanvasSize = new Vector2(canvasSize.Width, canvasSize.Height),
            OutputSize = new Vector2(output.OutputSize.Width, output.OutputSize.Height),
            LetterboxColor = new Vector4(letterbox.R, letterbox.G, letterbox.B, letterbox.A),
            LayoutMode = (int)output.CanvasLayoutMode
        };
    }

    public static MediaForgeSolidPushConstants BuildSolid(RenderSolidDrawObjectSnapshot solid)
    {
        var color = solid.FillColor;
        return new MediaForgeSolidPushConstants
        {
            FillColor = ToVector4(color),
            CropRect = ToVector4(solid.EffectiveCrop),
            BoxSize = new Vector2(solid.Transform.Size.Width, solid.Transform.Size.Height),
            Pivot = new Vector2(solid.Transform.Pivot.X, solid.Transform.Pivot.Y),
            Opacity = solid.Opacity,
            RotationDegrees = solid.Transform.RotationDegrees
        };
    }

    private static Vector4 ToVector4(ColorRgba color) =>
        new(color.R, color.G, color.B, color.A);

    private static Vector4 ToVector4(NormalizedRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static MediaForgeCanvasCompositePushConstants BuildCanvasComposite(
        RenderCanvasDrawObjectSnapshot canvas) =>
        new()
        {
            CropRect = ToVector4(canvas.EffectiveCrop),
            BoxSize = new Vector2(canvas.Transform.Size.Width, canvas.Transform.Size.Height),
            Pivot = new Vector2(canvas.Transform.Pivot.X, canvas.Transform.Pivot.Y),
            Opacity = canvas.Opacity,
            RotationDegrees = canvas.Transform.RotationDegrees
        };
}
