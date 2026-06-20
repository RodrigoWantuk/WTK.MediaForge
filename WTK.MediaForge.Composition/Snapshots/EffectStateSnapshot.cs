using System.Collections.Immutable;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ChromaKeyEffectSnapshot), "effect.chroma")]
[JsonDerivedType(typeof(ColorCorrectionEffectSnapshot), "effect.color")]
[JsonDerivedType(typeof(BlurEffectSnapshot), "effect.blur")]
[JsonDerivedType(typeof(TransitionEffectSnapshot), "effect.transition")]
internal abstract class EffectStateSnapshot
{
    public EffectId Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public int Order { get; init; }

    public int SchemaVersion { get; init; } = 1;
}

internal sealed class ChromaKeyEffectSnapshot : EffectStateSnapshot
{
    public ColorRgba KeyColor { get; init; } = ColorRgba.From(0f, 1f, 0f, 1f);

    public float Similarity { get; init; } = 0.4f;

    public float Smoothness { get; init; } = 0.08f;

    public float SpillReduction { get; init; } = 0.5f;
}

internal sealed class ColorCorrectionEffectSnapshot : EffectStateSnapshot
{
    public float Brightness { get; init; }

    public float Contrast { get; init; } = 1f;

    public float Saturation { get; init; } = 1f;

    public float HueDegrees { get; init; }
}

internal sealed class BlurEffectSnapshot : EffectStateSnapshot
{
    public float Radius { get; init; } = 4f;
}

internal sealed class TransitionEffectSnapshot : EffectStateSnapshot
{
    public TransitionKind Kind { get; init; } = TransitionKind.Fade;

    public float Progress { get; init; }

    public float DurationSeconds { get; init; } = 1f;
}

internal static class EffectSnapshotFactory
{
    public static ImmutableArray<EffectStateSnapshot> CloneEffects(IReadOnlyList<MediaForgeEffect> effects) =>
        effects.Select(CloneEffect).ToImmutableArray();

    private static EffectStateSnapshot CloneEffect(MediaForgeEffect effect) =>
        effect switch
        {
            ChromaKeyEffect chroma => new ChromaKeyEffectSnapshot
            {
                Id = chroma.Id,
                Name = chroma.Name,
                Enabled = chroma.Enabled,
                Order = chroma.Order,
                SchemaVersion = chroma.SchemaVersion,
                KeyColor = chroma.KeyColor,
                Similarity = chroma.Similarity,
                Smoothness = chroma.Smoothness,
                SpillReduction = chroma.SpillReduction
            },
            ColorCorrectionEffect color => new ColorCorrectionEffectSnapshot
            {
                Id = color.Id,
                Name = color.Name,
                Enabled = color.Enabled,
                Order = color.Order,
                SchemaVersion = color.SchemaVersion,
                Brightness = color.Brightness,
                Contrast = color.Contrast,
                Saturation = color.Saturation,
                HueDegrees = color.HueDegrees
            },
            BlurEffect blur => new BlurEffectSnapshot
            {
                Id = blur.Id,
                Name = blur.Name,
                Enabled = blur.Enabled,
                Order = blur.Order,
                SchemaVersion = blur.SchemaVersion,
                Radius = blur.Radius
            },
            TransitionEffect transition => new TransitionEffectSnapshot
            {
                Id = transition.Id,
                Name = transition.Name,
                Enabled = transition.Enabled,
                Order = transition.Order,
                SchemaVersion = transition.SchemaVersion,
                Kind = transition.Kind,
                Progress = transition.Progress,
                DurationSeconds = transition.DurationSeconds
            },
            _ => throw new NotSupportedException($"Unsupported effect type: {effect.GetType().Name}.")
        };
}
