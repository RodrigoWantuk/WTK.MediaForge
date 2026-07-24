using System.Security.Cryptography;
using System.Text.Json;

namespace WTK.MediaForge.Composition.Snapshots;

internal static class CanvasVisualStateFingerprint
{
    public static string Create(CanvasStateSnapshot canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("id", canvas.Id.Value);
            writer.WriteNumber("width", canvas.Size.Width);
            writer.WriteNumber("height", canvas.Size.Height);
            WriteColor(writer, canvas.BackgroundColor);
            WriteEffects(writer, canvas.Effects);
            writer.WriteStartArray("layers");
            for (var index = 0; index < canvas.Objects.Length; index++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("position", index);
                writer.WriteString("visualState", DrawObjectVisualStateFingerprint.Create(canvas.Objects[index]));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static string Create(RenderCanvasSnapshot canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("id", canvas.Id.Value);
            writer.WriteNumber("width", canvas.Size.Width);
            writer.WriteNumber("height", canvas.Size.Height);
            WriteColor(writer, canvas.BackgroundColor);
            WriteEffects(writer, canvas.Effects);
            writer.WriteStartArray("layers");
            for (var index = 0; index < canvas.Objects.Length; index++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("position", index);
                writer.WriteString("visualState", DrawObjectVisualStateFingerprint.Create(canvas.Objects[index]));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static void WriteColor(Utf8JsonWriter writer, Core.Color.ColorRgba color)
    {
        writer.WriteStartObject("background");
        writer.WriteNumber("r", color.R);
        writer.WriteNumber("g", color.G);
        writer.WriteNumber("b", color.B);
        writer.WriteNumber("a", color.A);
        writer.WriteEndObject();
    }

    private static void WriteEffects(Utf8JsonWriter writer, IEnumerable<EffectStateSnapshot> effects)
    {
        writer.WriteStartArray("effects");
        foreach (var effect in EffectStateFingerprint.CreateSequence(effects))
            writer.WriteStringValue(effect);
        writer.WriteEndArray();
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
