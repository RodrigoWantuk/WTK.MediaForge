using System.Numerics;
using System.Runtime.InteropServices;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

[StructLayout(LayoutKind.Explicit, Size = 48)]
internal struct MediaForgeMaskCompositePushConstants
{
    [FieldOffset(0)]
    public Vector4 Bounds;

    [FieldOffset(16)]
    public Vector4 Parameters;

    [FieldOffset(32)]
    public int ShapeKind;
}

internal static partial class CompositionPushConstantsBuilder
{
    public static MediaForgeMaskCompositePushConstants BuildMaskComposite(EffectMaskStateSnapshot mask) =>
        new()
        {
            Bounds = new Vector4(mask.Bounds.Left, mask.Bounds.Top, mask.Bounds.Right, mask.Bounds.Bottom),
            Parameters = new Vector4(
                mask.Feather,
                mask.Opacity,
                mask.Invert ? 1f : 0f,
                mask is RoundedRectangleEffectMaskStateSnapshot rounded ? rounded.CornerRadius : 0f),
            ShapeKind = mask switch
            {
                RectangleEffectMaskStateSnapshot => 0,
                RoundedRectangleEffectMaskStateSnapshot => 1,
                EllipseEffectMaskStateSnapshot => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(mask), mask.GetType().Name, "The mask is not a geometric Vulkan mask.")
            }
        };
}
