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
public abstract class EffectMask
{
    public bool Enabled { get; set; } = true;

    public bool Invert { get; set; }

    public float Feather { get; set; }

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
