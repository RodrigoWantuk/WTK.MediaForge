using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Sources;

public static class MediaSourceSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly IReadOnlyDictionary<string, Type> SettingsTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [MediaSourceTypes.Desktop.Value] = typeof(DesktopCaptureSourceSettings),
            [MediaSourceTypes.Webcam.Value] = typeof(WebcamSourceSettings),
            [MediaSourceTypes.NdiInput.Value] = typeof(NdiInputSourceSettings),
            [MediaSourceTypes.RtspInput.Value] = typeof(RtspInputSourceSettings),
            [MediaSourceTypes.VideoFile.Value] = typeof(VideoFileSourceSettings),
            [MediaSourceTypes.ImageFile.Value] = typeof(ImageFileSourceSettings),
            [MediaSourceTypes.WindowCapture.Value] = typeof(WindowCaptureSourceSettings),
            [MediaSourceTypes.Generated.Value] = typeof(GeneratedSourceSettings)
        };

    public static JsonObject ToJson(IMediaSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var node = JsonSerializer.SerializeToNode(settings, settings.GetType(), Options);
        return node as JsonObject ?? new JsonObject();
    }

    public static IMediaSourceSettings Deserialize(MediaSourceTypeId typeId, JsonObject? settings)
    {
        if (!TryDeserialize(typeId, settings, out var result, out var issue))
            throw new InvalidOperationException(issue?.Message ?? "Source settings could not be deserialized.");

        return result!;
    }

    public static bool TryDeserialize(
        MediaSourceTypeId typeId,
        JsonObject? settings,
        out IMediaSourceSettings? result,
        out ValidationIssue? issue)
    {
        result = null;
        issue = null;

        var canonical = MediaSourceTypeRegistry.ResolveCanonical(typeId);
        if (!SettingsTypes.TryGetValue(canonical.Value, out var settingsType))
        {
            issue = ValidationIssue.Error(
                "source.settings.unsupported",
                $"No settings schema registered for source type '{canonical.Value}'.");
            return false;
        }

        try
        {
            if (settings is null)
            {
                result = (IMediaSourceSettings)Activator.CreateInstance(settingsType)!;
                return true;
            }

            result = (IMediaSourceSettings?)settings.Deserialize(settingsType, Options);
            if (result is null)
            {
                issue = ValidationIssue.Error(
                    "source.settings.invalid",
                    $"Settings for source type '{canonical.Value}' could not be deserialized.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            issue = ValidationIssue.Error(
                "source.settings.invalid",
                $"Settings for source type '{canonical.Value}' are invalid: {ex.Message}");
            return false;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
