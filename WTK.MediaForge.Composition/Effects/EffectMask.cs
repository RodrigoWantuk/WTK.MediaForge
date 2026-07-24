using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Composition.Effects;

/// <summary>
/// Declarative mask applied to an effect result.  The coordinates are local to
/// the effect input, so the same definition has identical semantics for a
/// source, layer, or fully composed canvas.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RectangleEffectMask), "mask.rectangle")]
[JsonDerivedType(typeof(RoundedRectangleEffectMask), "mask.rounded-rectangle")]
[JsonDerivedType(typeof(EllipseEffectMask), "mask.ellipse")]
[JsonDerivedType(typeof(ImageAlphaEffectMask), "mask.image-alpha")]
[JsonDerivedType(typeof(LumaEffectMask), "mask.luma")]
[JsonDerivedType(typeof(GradientEffectMask), "mask.gradient")]
public abstract class EffectMask
{
    public bool Enabled { get; set; } = true;

    public bool Invert { get; set; }

    /// <summary>Opacity applied to the mask coverage after feather and inversion.</summary>
    public float Opacity { get; set; } = 1f;

    public float Feather { get; set; }

    /// <summary>Coordinate system used to evaluate the mask geometry.</summary>
    public EffectMaskCoordinateSpace CoordinateSpace { get; set; } = EffectMaskCoordinateSpace.EffectInput;

    public NormalizedRect Bounds { get; set; } = NormalizedRect.Full;

    public Transform2D Transform { get; set; } = Transform2D.Default;
}

public sealed class RectangleEffectMask : EffectMask;

public sealed class RoundedRectangleEffectMask : EffectMask
{
    /// <summary>Corner radius normalized to the shortest mask edge.</summary>
    public float CornerRadius { get; set; }
}

public sealed class EllipseEffectMask : EffectMask;

public sealed class ImageAlphaEffectMask : EffectMask
{
    /// <summary>Project-relative path to a static image asset whose alpha is sampled on GPU.</summary>
    public string AssetPath { get; set; } = string.Empty;
}

public sealed class LumaEffectMask : EffectMask
{
    /// <summary>Project-relative path to a static image asset whose luma is sampled on GPU.</summary>
    public string AssetPath { get; set; } = string.Empty;
}

public sealed class GradientEffectMask : EffectMask
{
    public NormalizedPoint Start { get; set; } = new(0f, 0f);

    public NormalizedPoint End { get; set; } = new(1f, 1f);

    public float StartOpacity { get; set; } = 1f;

    public float EndOpacity { get; set; }
}

public enum EffectMaskCoordinateSpace
{
    EffectInput = 0,
    Canvas = 1
}
