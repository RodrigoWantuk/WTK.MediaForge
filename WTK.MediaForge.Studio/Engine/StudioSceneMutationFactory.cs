using System.Globalization;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.Engine;

public static class StudioSceneMutationFactory
{
    public static SceneMutationPatch.SetLayerTransform SetLayerTransform(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new SceneMutationPatch.SetLayerTransform(
            StudioEngineIdMap.DrawObjectId(layer.Id),
            ToTransform(layer));
    }

    public static SceneMutationPatch.SetLayerBounds SetLayerBounds(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new SceneMutationPatch.SetLayerBounds(
            StudioEngineIdMap.DrawObjectId(layer.Id),
            new CanvasRect(
                ToFiniteSingle(layer.Transform.X, nameof(layer.Transform.X)),
                ToFiniteSingle(layer.Transform.Y, nameof(layer.Transform.Y)),
                ToPositiveSingle(layer.Transform.Width, nameof(layer.Transform.Width)),
                ToPositiveSingle(layer.Transform.Height, nameof(layer.Transform.Height))));
    }

    public static SceneMutationPatch.SetLayerVisibility SetLayerVisibility(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new SceneMutationPatch.SetLayerVisibility(
            StudioEngineIdMap.DrawObjectId(layer.Id),
            layer.IsVisible);
    }

    public static SceneMutationPatch.SetLayerOpacity SetLayerOpacity(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new SceneMutationPatch.SetLayerOpacity(
            StudioEngineIdMap.DrawObjectId(layer.Id),
            ToOpacity(layer.Transform.Opacity));
    }

    public static SceneMutationPatch.SetLayerEffects SetLayerEffects(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new SceneMutationPatch.SetLayerEffects(
            StudioEngineIdMap.DrawObjectId(layer.Id),
            layer.Effects.Select((effect, order) => ToEffect(effect, order)).ToArray());
    }

    public static Transform2D ToTransform(StudioLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new Transform2D
        {
            Position = new CanvasPoint(
                ToFiniteSingle(layer.Transform.X, nameof(layer.Transform.X)),
                ToFiniteSingle(layer.Transform.Y, nameof(layer.Transform.Y))),
            Size = new CanvasSize(
                ToPositiveSingle(layer.Transform.Width, nameof(layer.Transform.Width)),
                ToPositiveSingle(layer.Transform.Height, nameof(layer.Transform.Height))),
            RotationDegrees = ToFiniteSingle(layer.Transform.RotationDegrees, nameof(layer.Transform.RotationDegrees)),
            Pivot = NormalizedPoint.Center
        };
    }

    public static MediaForgeEffect ToEffect(StudioEffect effect, int order = 0)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (IsChromaKey(effect))
        {
            return new ChromaKeyEffect
            {
                Id = StudioEngineIdMap.EffectId(effect.Id),
                Name = effect.Name,
                Enabled = effect.IsEnabled,
                Order = order,
                SchemaVersion = 1,
                KeyColor = ParseColor(effect.KeyColor),
                Similarity = ToUnitSingle(effect.Tolerance, nameof(effect.Tolerance)),
                Smoothness = ToUnitSingle(effect.EdgeSmooth, nameof(effect.EdgeSmooth)),
                SpillReduction = ToUnitSingle(effect.Spill, nameof(effect.Spill))
            };
        }

        if (IsBlur(effect))
        {
            return new BlurEffect
            {
                Id = StudioEngineIdMap.EffectId(effect.Id),
                Name = effect.Name,
                Enabled = effect.IsEnabled,
                Order = order,
                SchemaVersion = 1,
                Radius = ToNonNegativeSingle(effect.Tolerance * 64d, nameof(effect.Tolerance))
            };
        }

        throw new NotSupportedException(
            $"Studio effect '{effect.Name}' ({effect.Id}) cannot be mapped to an engine effect yet.");
    }

    public static ColorRgba ParseColor(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length is not (6 or 8))
            throw new FormatException($"Color '{value}' must be #RRGGBB or #RRGGBBAA.");

        var r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var a = hex.Length == 8
            ? byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.MaxValue;

        return ColorRgba.From(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    public static float ToOpacity(double opacityPercent) =>
        ToUnitSingle(opacityPercent / 100d, nameof(opacityPercent));

    private static bool IsChromaKey(StudioEffect effect) =>
        effect.Id.Contains("chroma", StringComparison.OrdinalIgnoreCase) ||
        effect.Name.Contains("chroma", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlur(StudioEffect effect) =>
        effect.Id.Contains("blur", StringComparison.OrdinalIgnoreCase) ||
        effect.Name.Contains("blur", StringComparison.OrdinalIgnoreCase) ||
        effect.Name.Contains("desfoque", StringComparison.OrdinalIgnoreCase);

    private static float ToFiniteSingle(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException($"{name} must be finite.", name);

        return checked((float)value);
    }

    private static float ToPositiveSingle(double value, string name)
    {
        var single = ToFiniteSingle(value, name);
        if (single <= 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be positive.");

        return single;
    }

    private static float ToNonNegativeSingle(double value, string name)
    {
        var single = ToFiniteSingle(value, name);
        if (single < 0)
            throw new ArgumentOutOfRangeException(name, $"{name} must be non-negative.");

        return single;
    }

    private static float ToUnitSingle(double value, string name)
    {
        var single = ToFiniteSingle(value, name);
        if (single is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 1.");

        return single;
    }
}
