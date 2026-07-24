using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Composition.Snapshots;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RectangleEffectMaskStateSnapshot), "mask.rectangle")]
[JsonDerivedType(typeof(RoundedRectangleEffectMaskStateSnapshot), "mask.rounded-rectangle")]
[JsonDerivedType(typeof(EllipseEffectMaskStateSnapshot), "mask.ellipse")]
[JsonDerivedType(typeof(ImageAlphaEffectMaskStateSnapshot), "mask.image-alpha")]
[JsonDerivedType(typeof(LumaEffectMaskStateSnapshot), "mask.luma")]
[JsonDerivedType(typeof(GradientEffectMaskStateSnapshot), "mask.gradient")]
internal abstract class EffectMaskStateSnapshot
{
    public bool Enabled { get; init; } = true;
    public bool Invert { get; init; }
    public float Opacity { get; init; } = 1f;
    public float Feather { get; init; }
    public EffectMaskCoordinateSpace CoordinateSpace { get; init; } = EffectMaskCoordinateSpace.EffectInput;
    public NormalizedRect Bounds { get; init; } = NormalizedRect.Full;
    public Transform2D Transform { get; init; } = Transform2D.Default;
}

internal sealed class RectangleEffectMaskStateSnapshot : EffectMaskStateSnapshot;

internal sealed class RoundedRectangleEffectMaskStateSnapshot : EffectMaskStateSnapshot
{
    public float CornerRadius { get; init; }
}

internal sealed class EllipseEffectMaskStateSnapshot : EffectMaskStateSnapshot;

internal sealed class ImageAlphaEffectMaskStateSnapshot : EffectMaskStateSnapshot
{
    public string AssetPath { get; init; } = string.Empty;
}

internal sealed class LumaEffectMaskStateSnapshot : EffectMaskStateSnapshot
{
    public string AssetPath { get; init; } = string.Empty;
}

internal sealed class GradientEffectMaskStateSnapshot : EffectMaskStateSnapshot
{
    public NormalizedPoint Start { get; init; }
    public NormalizedPoint End { get; init; }
    public float StartOpacity { get; init; }
    public float EndOpacity { get; init; }
}

internal static class EffectMaskSnapshotFactory
{
    public static EffectMaskStateSnapshot? Clone(EffectMask? mask) =>
        mask switch
        {
            null => null,
            RectangleEffectMask rectangle => Copy(rectangle, new RectangleEffectMaskStateSnapshot()),
            RoundedRectangleEffectMask rounded => Copy(rounded, new RoundedRectangleEffectMaskStateSnapshot
            {
                CornerRadius = rounded.CornerRadius
            }),
            EllipseEffectMask ellipse => Copy(ellipse, new EllipseEffectMaskStateSnapshot()),
            ImageAlphaEffectMask image => Copy(image, new ImageAlphaEffectMaskStateSnapshot
            {
                AssetPath = image.AssetPath
            }),
            LumaEffectMask luma => Copy(luma, new LumaEffectMaskStateSnapshot
            {
                AssetPath = luma.AssetPath
            }),
            GradientEffectMask gradient => Copy(gradient, new GradientEffectMaskStateSnapshot
            {
                Start = gradient.Start,
                End = gradient.End,
                StartOpacity = gradient.StartOpacity,
                EndOpacity = gradient.EndOpacity
            }),
            _ => throw new NotSupportedException($"Unsupported effect mask type: {mask.GetType().Name}.")
        };

    private static T Copy<T>(EffectMask source, T target)
        where T : EffectMaskStateSnapshot => target switch
        {
            RectangleEffectMaskStateSnapshot => new RectangleEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform
            } as T ?? target,
            RoundedRectangleEffectMaskStateSnapshot rounded => new RoundedRectangleEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform, CornerRadius = rounded.CornerRadius
            } as T ?? target,
            EllipseEffectMaskStateSnapshot => new EllipseEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform
            } as T ?? target,
            ImageAlphaEffectMaskStateSnapshot image => new ImageAlphaEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform, AssetPath = image.AssetPath
            } as T ?? target,
            LumaEffectMaskStateSnapshot luma => new LumaEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform, AssetPath = luma.AssetPath
            } as T ?? target,
            GradientEffectMaskStateSnapshot gradient => new GradientEffectMaskStateSnapshot
            {
                Enabled = source.Enabled, Invert = source.Invert, Opacity = source.Opacity, Feather = source.Feather, CoordinateSpace = source.CoordinateSpace,
                Bounds = source.Bounds, Transform = source.Transform, Start = gradient.Start, End = gradient.End,
                StartOpacity = gradient.StartOpacity, EndOpacity = gradient.EndOpacity
            } as T ?? target,
            _ => throw new NotSupportedException($"Unsupported effect mask snapshot type: {target.GetType().Name}.")
        };
}
