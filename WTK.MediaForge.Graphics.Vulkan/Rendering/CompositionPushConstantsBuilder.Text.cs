using System.Numerics;
using System.Runtime.InteropServices;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 80)]
internal struct MediaForgeTextPushConstants
{
    [FieldOffset(0)]
    public Vector4 TextColor;

    [FieldOffset(16)]
    public Vector4 CropRect;

    [FieldOffset(32)]
    public Vector4 GeometryRect;

    [FieldOffset(48)]
    public Vector2 BoxSize;

    [FieldOffset(56)]
    public Vector2 Pivot;

    [FieldOffset(64)]
    public float Opacity;

    [FieldOffset(68)]
    public float RotationDegrees;
}

internal static partial class CompositionPushConstantsBuilder
{
    public static MediaForgeTextPushConstants BuildText(RenderTextDrawObjectSnapshot text) =>
        new()
        {
            TextColor = ToVector4(text.TextColor),
            CropRect = ToVector4(text.EffectiveCrop),
            BoxSize = new Vector2(text.Transform.Size.Width, text.Transform.Size.Height),
            Pivot = new Vector2(text.Transform.Pivot.X, text.Transform.Pivot.Y),
            Opacity = text.Opacity,
            RotationDegrees = text.Transform.RotationDegrees
        };

    public static MediaForgeBlurPushConstants BuildBlur(
        FrameSize textureSize,
        float radius,
        bool horizontal) =>
        new()
        {
            TexelSize = new Vector2(
                1f / MathF.Max(textureSize.Width, 1),
                1f / MathF.Max(textureSize.Height, 1)),
            Direction = horizontal ? new Vector2(1, 0) : new Vector2(0, 1),
            Radius = radius
        };
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct MediaForgeBlurPushConstants
{
    [FieldOffset(0)]
    public Vector2 TexelSize;

    [FieldOffset(8)]
    public Vector2 Direction;

    [FieldOffset(16)]
    public float Radius;
}
