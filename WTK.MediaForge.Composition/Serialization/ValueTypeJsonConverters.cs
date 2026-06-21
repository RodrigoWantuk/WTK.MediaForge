using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Composition.Serialization;

internal sealed class ColorRgbaJsonConverter : JsonConverter<ColorRgba>
{
    public override ColorRgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for ColorRgba.");

        float r = 0, g = 0, b = 0, a = 1;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();

            switch (property.ToLowerInvariant())
            {
                case "r": r = reader.GetSingle(); break;
                case "g": g = reader.GetSingle(); break;
                case "b": b = reader.GetSingle(); break;
                case "a": a = reader.GetSingle(); break;
            }
        }

        return new ColorRgba(r, g, b, a);
    }

    public override void Write(Utf8JsonWriter writer, ColorRgba value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("r", value.R);
        writer.WriteNumber("g", value.G);
        writer.WriteNumber("b", value.B);
        writer.WriteNumber("a", value.A);
        writer.WriteEndObject();
    }
}

internal sealed class FrameSizeJsonConverter : JsonConverter<FrameSize>
{
    public override FrameSize Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for FrameSize.");

        uint width = 0;
        uint height = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();

            switch (property.ToLowerInvariant())
            {
                case "width": width = reader.GetUInt32(); break;
                case "height": height = reader.GetUInt32(); break;
            }
        }

        return new FrameSize(width, height);
    }

    public override void Write(Utf8JsonWriter writer, FrameSize value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}

internal sealed class NormalizedRectJsonConverter : JsonConverter<NormalizedRect>
{
    public override NormalizedRect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for NormalizedRect.");

        float left = 0, top = 0, right = 1, bottom = 1;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();

            switch (property.ToLowerInvariant())
            {
                case "left": left = reader.GetSingle(); break;
                case "top": top = reader.GetSingle(); break;
                case "right": right = reader.GetSingle(); break;
                case "bottom": bottom = reader.GetSingle(); break;
            }
        }

        return new NormalizedRect(left, top, right, bottom);
    }

    public override void Write(Utf8JsonWriter writer, NormalizedRect value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("left", value.Left);
        writer.WriteNumber("top", value.Top);
        writer.WriteNumber("right", value.Right);
        writer.WriteNumber("bottom", value.Bottom);
        writer.WriteEndObject();
    }
}
