using System.Text.Json;
using System.Text.Json.Serialization;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Outputs;

internal sealed class H264ProfileJsonConverter : JsonConverter<H264Profile>
{
    public override H264Profile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("H.264 profile must be Baseline, Main, or High.");

        var value = reader.GetString()?.Trim().ToUpperInvariant() switch
        {
            "BASELINE" => H264Profile.Baseline,
            "MAIN" => H264Profile.Main,
            "HIGH" => H264Profile.High,
            _ => (H264Profile?)null
        };
        if (value is null)
        {
            throw new JsonException("H.264 profile must be Baseline, Main, or High.");
        }

        return value.Value;
    }

    public override void Write(Utf8JsonWriter writer, H264Profile value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
            throw new JsonException($"Unsupported H.264 profile '{value}'.");

        writer.WriteStringValue(value.ToString());
    }
}

internal sealed class H264LevelJsonConverter : JsonConverter<H264Level>
{
    private static readonly IReadOnlyDictionary<string, H264Level> Values =
        new Dictionary<string, H264Level>(StringComparer.OrdinalIgnoreCase)
        {
            ["3.0"] = H264Level.Level30,
            ["3.1"] = H264Level.Level31,
            ["3.2"] = H264Level.Level32,
            ["4.0"] = H264Level.Level40,
            ["4.1"] = H264Level.Level41,
            ["4.2"] = H264Level.Level42,
            ["5.0"] = H264Level.Level50,
            ["5.1"] = H264Level.Level51,
            ["5.2"] = H264Level.Level52
        };

    public override H264Level Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("H.264 level must be a string such as '4.2'.");

        var text = reader.GetString();
        if (text is not null && Values.TryGetValue(text, out var value))
            return value;

        if (text?.StartsWith("Level", StringComparison.OrdinalIgnoreCase) == true &&
            Enum.TryParse<H264Level>(text, ignoreCase: true, out value) &&
            Enum.IsDefined(value))
            return value;

        throw new JsonException("H.264 level must be one of 3.0-3.2, 4.0-4.2, or 5.0-5.2.");
    }

    public override void Write(Utf8JsonWriter writer, H264Level value, JsonSerializerOptions options)
    {
        var text = Values.FirstOrDefault(pair => pair.Value == value).Key;
        if (text is null)
            throw new JsonException($"Unsupported H.264 level '{value}'.");

        writer.WriteStringValue(text);
    }
}
