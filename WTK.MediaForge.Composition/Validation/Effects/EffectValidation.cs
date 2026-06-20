using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;

namespace WTK.MediaForge.Composition.Validation.Effects;

internal static class EffectValidation
{
    public static IEnumerable<ValidationIssue> ValidateDrawObjectEffects(
        MediaForgeDrawObject drawObject,
        string canvasName)
    {
        var seenIds = new HashSet<Guid>();

        foreach (var effect in drawObject.Effects)
        {
            if (effect.Id.IsEmpty)
            {
                yield return ValidationIssue.Error(
                    "effect.id.empty",
                    $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has an effect with empty id.");
            }
            else if (!seenIds.Add(effect.Id.Value))
            {
                yield return ValidationIssue.Error(
                    "effect.id.duplicate",
                    $"Draw object '{drawObject.Name}' in canvas '{canvasName}' has duplicate effect id {effect.Id}.");
            }

            if (effect.SchemaVersion <= 0)
            {
                yield return ValidationIssue.Error(
                    "effect.schema.invalid",
                    $"Effect '{effect.Name}' on draw object '{drawObject.Name}' has invalid SchemaVersion.");
            }

            if (effect.Order < 0)
            {
                yield return ValidationIssue.Error(
                    "effect.order.invalid",
                    $"Effect '{effect.Name}' on draw object '{drawObject.Name}' has negative Order.");
            }

            foreach (var issue in ValidateEffect(effect, drawObject.Name, canvasName))
                yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateEffect(
        MediaForgeEffect effect,
        string drawObjectName,
        string canvasName)
    {
        switch (effect)
        {
            case ChromaKeyEffect chroma:
                foreach (var issue in ValidateUnitRange(chroma.Similarity, drawObjectName, canvasName, effect.Name, "effect.chroma.similarity", "Similarity"))
                    yield return issue;
                foreach (var issue in ValidateUnitRange(chroma.Smoothness, drawObjectName, canvasName, effect.Name, "effect.chroma.smoothness", "Smoothness"))
                    yield return issue;
                foreach (var issue in ValidateUnitRange(chroma.SpillReduction, drawObjectName, canvasName, effect.Name, "effect.chroma.spill", "SpillReduction"))
                    yield return issue;
                if (!chroma.KeyColor.IsInRange())
                {
                    yield return ValidationIssue.Error(
                        "effect.chroma.color",
                        $"Effect '{effect.Name}' on draw object '{drawObjectName}' in canvas '{canvasName}' has invalid KeyColor.");
                }
                break;

            case ColorCorrectionEffect color:
                foreach (var issue in ValidateFinite(color.Brightness, drawObjectName, canvasName, effect.Name, "effect.color.brightness", "Brightness"))
                    yield return issue;
                foreach (var issue in ValidatePositiveFinite(color.Contrast, drawObjectName, canvasName, effect.Name, "effect.color.contrast", "Contrast"))
                    yield return issue;
                foreach (var issue in ValidatePositiveFinite(color.Saturation, drawObjectName, canvasName, effect.Name, "effect.color.saturation", "Saturation"))
                    yield return issue;
                foreach (var issue in ValidateFinite(color.HueDegrees, drawObjectName, canvasName, effect.Name, "effect.color.hue", "HueDegrees"))
                    yield return issue;
                break;

            case BlurEffect blur:
                foreach (var issue in ValidatePositiveFinite(blur.Radius, drawObjectName, canvasName, effect.Name, "effect.blur.radius", "Radius"))
                    yield return issue;
                break;

            case TransitionEffect transition:
                foreach (var issue in ValidateUnitRange(transition.Progress, drawObjectName, canvasName, effect.Name, "effect.transition.progress", "Progress"))
                    yield return issue;
                foreach (var issue in ValidatePositiveFinite(transition.DurationSeconds, drawObjectName, canvasName, effect.Name, "effect.transition.duration", "DurationSeconds"))
                    yield return issue;
                break;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateUnitRange(
        float value,
        string drawObjectName,
        string canvasName,
        string effectName,
        string code,
        string fieldName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            yield return ValidationIssue.Error(
                code,
                $"Effect '{effectName}' on draw object '{drawObjectName}' in canvas '{canvasName}' has invalid {fieldName}; expected [0,1].");
        }
    }

    private static IEnumerable<ValidationIssue> ValidatePositiveFinite(
        float value,
        string drawObjectName,
        string canvasName,
        string effectName,
        string code,
        string fieldName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            yield return ValidationIssue.Error(
                code,
                $"Effect '{effectName}' on draw object '{drawObjectName}' in canvas '{canvasName}' has invalid {fieldName}; expected a positive finite number.");
        }
    }

    private static IEnumerable<ValidationIssue> ValidateFinite(
        float value,
        string drawObjectName,
        string canvasName,
        string effectName,
        string code,
        string fieldName)
    {
        if (!float.IsFinite(value))
        {
            yield return ValidationIssue.Error(
                code,
                $"Effect '{effectName}' on draw object '{drawObjectName}' in canvas '{canvasName}' has invalid {fieldName}; expected a finite number.");
        }
    }
}
