using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Effects;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ChromaKeyEffect), "effect.chroma")]
[JsonDerivedType(typeof(ColorCorrectionEffect), "effect.color")]
[JsonDerivedType(typeof(BlurEffect), "effect.blur")]
public abstract class MediaForgeEffect
{
    public EffectId Id { get; set; } = EffectId.New();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int Order { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public EffectMask? Mask { get; set; }
}

public sealed class ChromaKeyEffect : MediaForgeEffect
{
    public ColorRgba KeyColor { get; set; } = ColorRgba.From(0f, 1f, 0f, 1f);

    public float Similarity { get; set; } = 0.4f;

    public float Smoothness { get; set; } = 0.08f;

    public float SpillReduction { get; set; } = 0.5f;
}

public sealed class ColorCorrectionEffect : MediaForgeEffect
{
    public float Brightness { get; set; }

    public float Contrast { get; set; } = 1f;

    public float Saturation { get; set; } = 1f;

    public float HueDegrees { get; set; }
}

public sealed class BlurEffect : MediaForgeEffect
{
    public float Radius { get; set; } = 4f;
}
