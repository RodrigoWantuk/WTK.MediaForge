using System.Text.Json;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Services;

public sealed class StudioLayoutService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath { get; }

    public StudioLayoutService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        SettingsPath = Path.Combine(appData, "WTK", "MediaForge", "Studio", "settings.json");
    }

    public StudioLayoutDocument Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new StudioLayoutDocument();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StudioLayoutDocument>(json, SerializerOptions) ?? new StudioLayoutDocument();
        }
        catch (JsonException)
        {
            return new StudioLayoutDocument();
        }
        catch (IOException)
        {
            return new StudioLayoutDocument();
        }
    }

    public void Save(StudioLayoutDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(document, SerializerOptions));
    }
}
