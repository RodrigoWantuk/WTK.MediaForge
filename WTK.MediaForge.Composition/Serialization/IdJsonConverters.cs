using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Serialization;

internal sealed class SourceIdJsonConverter : JsonConverter<SourceId>
{
    public override SourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, SourceId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class CanvasIdJsonConverter : JsonConverter<CanvasId>
{
    public override CanvasId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, CanvasId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class DrawObjectIdJsonConverter : JsonConverter<DrawObjectId>
{
    public override DrawObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, DrawObjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class RenderOutputIdJsonConverter : JsonConverter<RenderOutputId>
{
    public override RenderOutputId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, RenderOutputId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class AudioSourceIdJsonConverter : JsonConverter<AudioSourceId>
{
    public override AudioSourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetGuid());
    public override void Write(Utf8JsonWriter writer, AudioSourceId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal sealed class AudioNodeIdJsonConverter : JsonConverter<AudioNodeId>
{
    public override AudioNodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetGuid());
    public override void Write(Utf8JsonWriter writer, AudioNodeId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal sealed class AudioBusIdJsonConverter : JsonConverter<AudioBusId>
{
    public override AudioBusId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetGuid());
    public override void Write(Utf8JsonWriter writer, AudioBusId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal sealed class AudioOutputRouteIdJsonConverter : JsonConverter<AudioOutputRouteId>
{
    public override AudioOutputRouteId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetGuid());
    public override void Write(Utf8JsonWriter writer, AudioOutputRouteId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal sealed class AudioSinkIdJsonConverter : JsonConverter<AudioSinkId>
{
    public override AudioSinkId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetGuid());
    public override void Write(Utf8JsonWriter writer, AudioSinkId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

internal sealed class MediaSourceTypeIdJsonConverter : JsonConverter<MediaSourceTypeId>
{
    public override MediaSourceTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, MediaSourceTypeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class EffectIdJsonConverter : JsonConverter<EffectId>
{
    public override EffectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, EffectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class RenderOutputTypeIdJsonConverter : JsonConverter<RenderOutputTypeId>
{
    public override RenderOutputTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, RenderOutputTypeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
