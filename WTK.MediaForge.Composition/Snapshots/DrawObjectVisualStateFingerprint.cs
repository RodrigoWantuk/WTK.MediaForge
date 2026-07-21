using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WTK.MediaForge.Composition.Scenes.Editing;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class DrawObjectVisualStateFingerprint
{
    public static string Create(DrawObjectStateSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(drawObject);
        return Hash(writer => Write(writer, drawObject));
    }

    public static string Create(RenderDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(drawObject);
        return Hash(writer => Write(writer, drawObject));
    }

    internal static void Write(Utf8JsonWriter writer, DrawObjectStateSnapshot drawObject)
    {
        writer.WriteStartObject();
        WriteCommon(writer, drawObject);
        switch (drawObject)
        {
            case SourceLayerDrawObjectSnapshot source:
                writer.WriteString("type", "source-layer");
                writer.WriteString("sourceId", source.SourceId.Value);
                writer.WriteNumber("layoutMode", (int)source.LayoutMode);
                WriteColor(writer, "letterbox", source.LetterboxColor);
                WriteNullableEnum(writer, "contentRotation", source.ContentRotationOverride);
                break;
            case TextDrawObjectSnapshot text:
                writer.WriteString("type", "text");
                writer.WriteString("text", text.Text);
                writer.WriteString("fontFamily", text.FontFamily);
                writer.WriteNumber("fontSize", text.FontSize);
                WriteColor(writer, "textColor", text.TextColor);
                break;
            case SolidDrawObjectSnapshot solid:
                writer.WriteString("type", "solid");
                WriteColor(writer, "fillColor", solid.FillColor);
                break;
            case CanvasDrawObjectSnapshot nested:
                writer.WriteString("type", "nested-canvas");
                writer.WriteString("nestedCanvasId", nested.NestedCanvasId.Value);
                WriteBinding(writer, nested.VersionBinding);
                break;
            default:
                throw new NotSupportedException(
                    $"Draw object type '{drawObject.GetType().FullName}' must define an explicit visual fingerprint.");
        }
        writer.WriteEndObject();
    }

    internal static void Write(Utf8JsonWriter writer, RenderDrawObjectSnapshot drawObject)
    {
        writer.WriteStartObject();
        WriteCommon(writer, drawObject);
        switch (drawObject)
        {
            case RenderSourceLayerDrawObjectSnapshot source:
                writer.WriteString("type", "source-layer");
                writer.WriteString("sourceId", source.SourceId.Value);
                writer.WriteNumber("layoutMode", (int)source.LayoutMode);
                WriteColor(writer, "letterbox", source.LetterboxColor);
                WriteNullableEnum(writer, "contentRotation", source.ContentRotationOverride);
                break;
            case RenderTextDrawObjectSnapshot text:
                writer.WriteString("type", "text");
                writer.WriteString("text", text.Text);
                writer.WriteString("fontFamily", text.FontFamily);
                writer.WriteNumber("fontSize", text.FontSize);
                WriteColor(writer, "textColor", text.TextColor);
                break;
            case RenderSolidDrawObjectSnapshot solid:
                writer.WriteString("type", "solid");
                WriteColor(writer, "fillColor", solid.FillColor);
                break;
            case RenderCanvasDrawObjectSnapshot nested:
                writer.WriteString("type", "nested-canvas");
                writer.WriteString("nestedCanvasId", nested.NestedCanvasId.Value);
                WriteBinding(writer, nested.VersionBinding);
                break;
            default:
                throw new NotSupportedException(
                    $"Render draw object type '{drawObject.GetType().FullName}' must define an explicit visual fingerprint.");
        }
        writer.WriteEndObject();
    }

    private static void WriteCommon(Utf8JsonWriter writer, DrawObjectStateSnapshot drawObject)
    {
        writer.WriteString("id", drawObject.Id.Value);
        writer.WriteBoolean("enabled", drawObject.Enabled);
        WriteTransform(writer, drawObject.Transform);
        writer.WriteNumber("opacity", drawObject.Opacity);
        writer.WriteNumber("blendMode", (int)drawObject.BlendMode);
        WriteCrop(writer, drawObject.Crop);
        WriteEffects(writer, drawObject.Effects);
    }

    private static void WriteCommon(Utf8JsonWriter writer, RenderDrawObjectSnapshot drawObject)
    {
        writer.WriteString("id", drawObject.Id.Value);
        writer.WriteBoolean("enabled", drawObject.Enabled);
        WriteTransform(writer, drawObject.Transform);
        writer.WriteNumber("opacity", drawObject.Opacity);
        writer.WriteNumber("blendMode", (int)drawObject.BlendMode);
        WriteCrop(writer, drawObject.EffectiveCrop);
        WriteEffects(writer, drawObject.Effects);
    }

    private static void WriteTransform(Utf8JsonWriter writer, Core.Geometry.Transform2D transform)
    {
        writer.WriteStartObject("transform");
        writer.WriteNumber("x", transform.Position.X);
        writer.WriteNumber("y", transform.Position.Y);
        writer.WriteNumber("width", transform.Size.Width);
        writer.WriteNumber("height", transform.Size.Height);
        writer.WriteNumber("pivotX", transform.Pivot.X);
        writer.WriteNumber("pivotY", transform.Pivot.Y);
        writer.WriteNumber("rotationDegrees", transform.RotationDegrees);
        writer.WriteEndObject();
    }

    private static void WriteCrop(Utf8JsonWriter writer, Core.Geometry.NormalizedRect? crop)
    {
        if (crop is null)
        {
            writer.WriteNull("crop");
            return;
        }

        writer.WriteStartObject("crop");
        writer.WriteNumber("left", crop.Value.Left);
        writer.WriteNumber("top", crop.Value.Top);
        writer.WriteNumber("right", crop.Value.Right);
        writer.WriteNumber("bottom", crop.Value.Bottom);
        writer.WriteEndObject();
    }

    private static void WriteEffects(Utf8JsonWriter writer, IEnumerable<EffectStateSnapshot> effects)
    {
        writer.WriteStartArray("effects");
        foreach (var effect in effects.OrderBy(static effect => effect.Order).ThenBy(static effect => effect.Id.Value))
            writer.WriteStringValue(EffectStateFingerprint.CreateSemanticConfiguration(effect));
        writer.WriteEndArray();
    }

    private static void WriteColor(Utf8JsonWriter writer, string name, Core.Color.ColorRgba color)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("r", color.R);
        writer.WriteNumber("g", color.G);
        writer.WriteNumber("b", color.B);
        writer.WriteNumber("a", color.A);
        writer.WriteEndObject();
    }

    private static void WriteNullableEnum<T>(Utf8JsonWriter writer, string name, T? value)
        where T : struct, Enum
    {
        if (value is null)
            writer.WriteNull(name);
        else
            writer.WriteNumber(name, Convert.ToInt32(value.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void WriteBinding(Utf8JsonWriter writer, SceneVersionBinding binding)
    {
        binding.Validate();
        writer.WriteStartObject("versionBinding");
        writer.WriteNumber("kind", (int)binding.Kind);
        if (binding.DraftSessionId is { } draft)
            writer.WriteString("draftSessionId", draft.Value);
        else
            writer.WriteNull("draftSessionId");
        if (binding.ExplicitVersionId is { } version)
            writer.WriteString("explicitVersionId", version.Value);
        else
            writer.WriteNull("explicitVersionId");
        writer.WriteEndObject();
    }

    private static string Hash(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
