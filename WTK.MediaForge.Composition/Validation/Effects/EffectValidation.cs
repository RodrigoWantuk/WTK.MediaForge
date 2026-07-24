using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;

namespace WTK.MediaForge.Composition.Validation.Effects;

internal static class EffectValidation
{
    private static readonly EffectCapabilityRegistry Capabilities = EffectCapabilityRegistry.Default;

    public static IEnumerable<ValidationIssue> ValidateDrawObjectEffects(
        MediaForgeDrawObject drawObject,
        string canvasName) =>
        ValidateStack(drawObject.Effects, EffectScope.Layer, drawObject.Name, canvasName);

    public static IEnumerable<ValidationIssue> ValidateStack(
        IEnumerable<MediaForgeEffect> effects,
        EffectScope scope,
        string ownerName,
        string ownerContainer)
    {
        var seenIds = new HashSet<Guid>();

        foreach (var effect in effects)
        {
            if (effect.Id.IsEmpty)
            {
                yield return ValidationIssue.Error(
                    "effect.id.empty",
                    $"'{ownerName}' in '{ownerContainer}' has an effect with empty id.");
            }
            else if (!seenIds.Add(effect.Id.Value))
            {
                yield return ValidationIssue.Error(
                    "effect.id.duplicate",
                    $"'{ownerName}' in '{ownerContainer}' has duplicate effect id {effect.Id}.");
            }

            if (effect.SchemaVersion <= 0)
            {
                yield return ValidationIssue.Error(
                    "effect.schema.invalid",
                    $"Effect '{effect.Name}' on '{ownerName}' has invalid SchemaVersion.");
            }

            if (effect.Order < 0)
            {
                yield return ValidationIssue.Error(
                    "effect.order.invalid",
                    $"Effect '{effect.Name}' on '{ownerName}' has negative Order.");
            }

            if (!Capabilities.TryGet(effect.GetType(), out var descriptor))
            {
                yield return ValidationIssue.Error(
                    "effect.capability.missing",
                    $"Effect '{effect.Name}' has no registered capability descriptor.");
            }
            else if (!descriptor.AcceptsScope(scope))
            {
                yield return ValidationIssue.Error(
                    "effect.scope.invalid",
                    $"Effect '{effect.Name}' does not support the {scope} scope.");
            }

            foreach (var issue in ValidateEffect(effect, ownerName, ownerContainer))
                yield return issue;

            foreach (var issue in ValidateMask(effect.Mask, effect.Name, ownerName, ownerContainer))
                yield return issue;
        }
    }

    private static IEnumerable<ValidationIssue> ValidateMask(
        EffectMask? mask,
        string effectName,
        string ownerName,
        string ownerContainer)
    {
        if (mask is null)
            yield break;

        if (!float.IsFinite(mask.Feather) || mask.Feather < 0f || mask.Feather > 1f)
        {
            yield return ValidationIssue.Error(
                "effect.mask.feather",
                $"Effect '{effectName}' on '{ownerName}' in '{ownerContainer}' has invalid mask feather; expected [0,1].");
        }

        if (!mask.Bounds.IsValid)
        {
            yield return ValidationIssue.Error(
                "effect.mask.bounds",
                $"Effect '{effectName}' on '{ownerName}' in '{ownerContainer}' has invalid normalized mask bounds.");
        }

        var transform = mask.Transform;
        if (!transform.HasPositiveSize ||
            !float.IsFinite(transform.Position.X) || !float.IsFinite(transform.Position.Y) ||
            !float.IsFinite(transform.Size.Width) || !float.IsFinite(transform.Size.Height) ||
            !float.IsFinite(transform.RotationDegrees) ||
            !float.IsFinite(transform.Pivot.X) || !float.IsFinite(transform.Pivot.Y))
        {
            yield return ValidationIssue.Error(
                "effect.mask.transform",
                $"Effect '{effectName}' on '{ownerName}' in '{ownerContainer}' has an invalid mask transform.");
        }

        switch (mask)
        {
            case RoundedRectangleEffectMask rounded when
                !float.IsFinite(rounded.CornerRadius) || rounded.CornerRadius < 0f || rounded.CornerRadius > 0.5f:
                yield return ValidationIssue.Error(
                    "effect.mask.corner_radius",
                    $"Effect '{effectName}' on '{ownerName}' in '{ownerContainer}' has invalid rounded-mask corner radius; expected [0,0.5].");
                break;
            case ImageAlphaEffectMask image when string.IsNullOrWhiteSpace(image.AssetPath):
                yield return ValidationIssue.Error(
                    "effect.mask.asset_path",
                    $"Effect '{effectName}' on '{ownerName}' in '{ownerContainer}' uses an image-alpha mask without an asset path.");
                break;
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
