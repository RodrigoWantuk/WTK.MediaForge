using System.Collections.Immutable;
using System.Globalization;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class EffectStateFingerprint
{
    public static string Create(EffectStateSnapshot effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var builder = new FingerprintBuilder();
        builder.Append("id", effect.Id.Value);
        builder.Append("name", effect.Name);
        builder.Append("enabled", effect.Enabled);
        builder.Append("order", effect.Order);
        builder.Append("schema", effect.SchemaVersion);
        AppendTypedParameters(builder, effect);

        return builder.ToString();
    }

    public static string CreateSemanticConfiguration(EffectStateSnapshot effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var builder = new FingerprintBuilder();
        builder.Append("enabled", effect.Enabled);
        builder.Append("order", effect.Order);
        builder.Append("schema", effect.SchemaVersion);
        AppendTypedParameters(builder, effect);

        return builder.ToString();
    }

    private static void AppendTypedParameters(FingerprintBuilder builder, EffectStateSnapshot effect)
    {
        switch (effect)
        {
            case ChromaKeyEffectSnapshot chroma:
                builder.Append("type", "effect.chroma");
                builder.Append("key.r", chroma.KeyColor.R);
                builder.Append("key.g", chroma.KeyColor.G);
                builder.Append("key.b", chroma.KeyColor.B);
                builder.Append("key.a", chroma.KeyColor.A);
                builder.Append("similarity", chroma.Similarity);
                builder.Append("smoothness", chroma.Smoothness);
                builder.Append("spill", chroma.SpillReduction);
                break;

            case ColorCorrectionEffectSnapshot color:
                builder.Append("type", "effect.color");
                builder.Append("brightness", color.Brightness);
                builder.Append("contrast", color.Contrast);
                builder.Append("saturation", color.Saturation);
                builder.Append("hue", color.HueDegrees);
                break;

            case BlurEffectSnapshot blur:
                builder.Append("type", "effect.blur");
                builder.Append("radius", blur.Radius);
                break;

            case TransitionEffectSnapshot transition:
                builder.Append("type", "effect.transition");
                builder.Append("kind", transition.Kind);
                builder.Append("progress", transition.Progress);
                builder.Append("duration", transition.DurationSeconds);
                break;

            default:
                builder.Append("type", effect.GetType().FullName ?? effect.GetType().Name);
                break;
        }
    }

    public static ImmutableArray<string> CreateSequence(IEnumerable<EffectStateSnapshot> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        return effects
            .OrderBy(static effect => effect.Order)
            .ThenBy(static effect => effect.Id.Value)
            .Select(Create)
            .ToImmutableArray();
    }

    public static bool SequenceEquals(
        ImmutableArray<EffectStateSnapshot> left,
        ImmutableArray<EffectStateSnapshot> right)
    {
        if (left.Length != right.Length)
            return false;

        var leftFingerprints = CreateSequence(left);
        var rightFingerprints = CreateSequence(right);

        return leftFingerprints.SequenceEqual(rightFingerprints, StringComparer.Ordinal);
    }

    private sealed class FingerprintBuilder
    {
        private readonly List<string> _parts = [];

        public void Append(string name, object? value) =>
            _parts.Add($"{name}={Format(value)}");

        public override string ToString() => string.Join("|", _parts);

        private static string Format(object? value) =>
            value switch
            {
                null => "<null>",
                float single => single.ToString("R", CultureInfo.InvariantCulture),
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                decimal number => number.ToString(CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
    }
}
