using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Serialization;

public sealed class SourceIdJsonConverter : JsonConverter<SourceId>
{
    public override SourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, SourceId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class CanvasIdJsonConverter : JsonConverter<CanvasId>
{
    public override CanvasId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, CanvasId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class DrawObjectIdJsonConverter : JsonConverter<DrawObjectId>
{
    public override DrawObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, DrawObjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class RenderOutputIdJsonConverter : JsonConverter<RenderOutputId>
{
    public override RenderOutputId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, RenderOutputId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class MediaSourceTypeIdJsonConverter : JsonConverter<MediaSourceTypeId>
{
    public override MediaSourceTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, MediaSourceTypeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed class RenderOutputTypeIdJsonConverter : JsonConverter<RenderOutputTypeId>
{
    public override RenderOutputTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, RenderOutputTypeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
