using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Geometry;

namespace WTK.MediaForge.Composition.Serialization;

internal sealed class Transform2DJsonConverter : JsonConverter<Transform2D>
{
    public override Transform2D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for Transform2D.");

        CanvasPoint position = CanvasPoint.Zero;
        CanvasSize size = CanvasSize.Empty;
        float rotationDegrees = 0;
        NormalizedPoint pivot = NormalizedPoint.TopLeft;

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
                case "position":
                    position = ReadPoint(ref reader);
                    break;
                case "size":
                    size = ReadSize(ref reader);
                    break;
                case "rotationdegrees":
                    rotationDegrees = reader.GetSingle();
                    break;
                case "pivot":
                    pivot = ReadNormalizedPoint(ref reader);
                    break;
            }
        }

        return new Transform2D
        {
            Position = position,
            Size = size,
            RotationDegrees = rotationDegrees,
            Pivot = pivot
        };
    }

    public override void Write(Utf8JsonWriter writer, Transform2D value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("position");
        WritePoint(writer, value.Position);
        writer.WritePropertyName("size");
        WriteSize(writer, value.Size);
        writer.WriteNumber("rotationDegrees", value.RotationDegrees);
        writer.WritePropertyName("pivot");
        WriteNormalizedPoint(writer, value.Pivot);
        writer.WriteEndObject();
    }

    private static CanvasPoint ReadPoint(ref Utf8JsonReader reader)
    {
        float x = 0, y = 0;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for CanvasPoint.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();
            if (property.Equals("x", StringComparison.OrdinalIgnoreCase))
                x = reader.GetSingle();
            else if (property.Equals("y", StringComparison.OrdinalIgnoreCase))
                y = reader.GetSingle();
        }

        return new CanvasPoint(x, y);
    }

    private static CanvasSize ReadSize(ref Utf8JsonReader reader)
    {
        float width = 0, height = 0;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for CanvasSize.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();
            if (property.Equals("width", StringComparison.OrdinalIgnoreCase))
                width = reader.GetSingle();
            else if (property.Equals("height", StringComparison.OrdinalIgnoreCase))
                height = reader.GetSingle();
        }

        return new CanvasSize(width, height);
    }

    private static NormalizedPoint ReadNormalizedPoint(ref Utf8JsonReader reader)
    {
        float x = 0, y = 0;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for NormalizedPoint.");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();
            if (property.Equals("x", StringComparison.OrdinalIgnoreCase))
                x = reader.GetSingle();
            else if (property.Equals("y", StringComparison.OrdinalIgnoreCase))
                y = reader.GetSingle();
        }

        return new NormalizedPoint(x, y);
    }

    private static void WritePoint(Utf8JsonWriter writer, CanvasPoint point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, CanvasSize size)
    {
        writer.WriteStartObject();
        writer.WriteNumber("width", size.Width);
        writer.WriteNumber("height", size.Height);
        writer.WriteEndObject();
    }

    private static void WriteNormalizedPoint(Utf8JsonWriter writer, NormalizedPoint point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }
}
