using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public static class RenderOutputSettingsSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static readonly IReadOnlyDictionary<string, Type> SettingsTypes =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [RenderOutputTypes.PreviewWindow.Value] = typeof(PreviewWindowOutputSettings),
            [RenderOutputTypes.Offscreen.Value] = typeof(OffscreenOutputSettings),
            [RenderOutputTypes.Ndi.Value] = typeof(NdiOutputSettings),
            [RenderOutputTypes.EncodedFile.Value] = typeof(EncodedFileOutputSettings),
            [RenderOutputTypes.RecordingMp4.Value] = typeof(RecordingMp4OutputSettings),
            [RenderOutputTypes.StreamingRtmp.Value] = typeof(StreamingRtmpOutputSettings),
            [RenderOutputTypes.StreamingSrt.Value] = typeof(StreamingSrtOutputSettings),
            [RenderOutputTypes.StreamingRtsp.Value] = typeof(StreamingRtspOutputSettings),
            [RenderOutputTypes.StreamingHls.Value] = typeof(StreamingHlsOutputSettings),
            [RenderOutputTypes.VirtualCamera.Value] = typeof(VirtualCameraOutputSettings)
        };

    public static JsonObject ToJson(IRenderOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var node = JsonSerializer.SerializeToNode(settings, settings.GetType(), Options);
        return node as JsonObject ?? new JsonObject();
    }

    public static IRenderOutputSettings Deserialize(RenderOutputTypeId typeId, JsonObject? settings)
    {
        if (!TryDeserialize(typeId, settings, out var result, out var issue))
            throw new InvalidOperationException(issue?.Message ?? "Output settings could not be deserialized.");

        return result!;
    }

    public static bool TryDeserialize(
        RenderOutputTypeId typeId,
        JsonObject? settings,
        out IRenderOutputSettings? result,
        out ValidationIssue? issue)
    {
        result = null;
        issue = null;

        if (!SettingsTypes.TryGetValue(typeId.Value, out var settingsType))
        {
            issue = ValidationIssue.Error(
                "output.settings.unsupported",
                $"No settings schema registered for output type '{typeId.Value}'.");
            return false;
        }

        try
        {
            if (settings is null)
            {
                result = (IRenderOutputSettings)Activator.CreateInstance(settingsType)!;
                return true;
            }

            result = (IRenderOutputSettings?)settings.Deserialize(settingsType, Options);
            if (result is null)
            {
                issue = ValidationIssue.Error(
                    "output.settings.invalid",
                    $"Settings for output type '{typeId.Value}' could not be deserialized.");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            issue = ValidationIssue.Error(
                "output.settings.invalid",
                $"Settings for output type '{typeId.Value}' are invalid: {ex.Message}");
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
