using System.Numerics;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class Cp1PushConstantsBuilder
{
    public static MediaForgeLayerPushConstants BuildSourceLayer(
        RenderSourceLayerDrawObjectSnapshot layer,
        GpuFrameReference frame,
        ChromaKeyEffectSnapshot? chromaKey)
    {
        var crop = layer.EffectiveCrop;
        var keyColor = chromaKey?.KeyColor ?? ColorRgba.Transparent;

        return new MediaForgeLayerPushConstants
        {
            CropRect = new Vector4(crop.Left, crop.Top, crop.Right, crop.Bottom),
            ChromaKeyColor = ToVector4(keyColor),
            ChromaKeyParameters = chromaKey is null
                ? Vector4.Zero
                : new Vector4(
                    chromaKey.Similarity,
                    chromaKey.Smoothness,
                    chromaKey.SpillReduction,
                    1f),
            LogicalSize = new Vector2(frame.LogicalSize.Width, frame.LogicalSize.Height),
            BoxSize = new Vector2(layer.Transform.Size.Width, layer.Transform.Size.Height),
            Pivot = new Vector2(layer.Transform.Pivot.X, layer.Transform.Pivot.Y),
            Opacity = layer.Opacity,
            LayoutMode = (int)layer.LayoutMode,
            ContentRotation = CapturePreviewGeometry.ResolveShaderRotation(
                frame.Rotation,
                frame.LogicalSize,
                frame.TextureSize)
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
            Opacity = solid.Opacity
        };
    }

    private static Vector4 ToVector4(ColorRgba color) =>
        new(color.R, color.G, color.B, color.A);

    public static MediaForgeCanvasCompositePushConstants BuildCanvasComposite(
        RenderCanvasDrawObjectSnapshot canvas) =>
        new()
        {
            Opacity = canvas.Opacity
        };
}
