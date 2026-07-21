using System.Text.Json;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Services;

public interface IStudioLayoutService
{
    StudioLayoutDocument Load();

    void Save(StudioLayoutDocument document);
}

public sealed class StudioLayoutService : IStudioLayoutService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string SettingsPath { get; }

    public StudioLayoutService(string? settingsPath = null)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            SettingsPath = settingsPath;
            return;
        }

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
            return Validate(JsonSerializer.Deserialize<StudioLayoutDocument>(json, SerializerOptions));
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
        ArgumentNullException.ThrowIfNull(document);

        Validate(document);
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(document, SerializerOptions));
    }

    private static StudioLayoutDocument Validate(StudioLayoutDocument? document)
    {
        document ??= new StudioLayoutDocument();
        document.Layout ??= new StudioLayoutState();
        document.Layout.LeftProportion = ClampProportion(document.Layout.LeftProportion, 0.20);
        document.Layout.RightProportion = ClampProportion(document.Layout.RightProportion, 0.25);
        document.Layout.ProductionProportion = ClampProportion(document.Layout.ProductionProportion, 0.36);
        document.Layout.PropertiesProportion = ClampProportion(document.Layout.PropertiesProportion, 0.64);
        document.Layout.BottomProportion = ClampProportion(document.Layout.BottomProportion, 0.28);
        document.Layout.LeftWidth = ClampPixels(document.Layout.LeftWidth, 280);
        document.Layout.RightWidth = ClampPixels(document.Layout.RightWidth, 360);
        document.Layout.BottomHeight = ClampPixels(document.Layout.BottomHeight, 240);
        document.Layout.Panels ??= StudioPanelLayoutState.CreateDefaults();
        document.Layout.FloatingDocks ??= [];

        foreach (var pair in StudioPanelLayoutState.CreateDefaults())
        {
            document.Layout.Panels.TryAdd(pair.Key, pair.Value);
        }

        return document;
    }

    private static double ClampProportion(double value, double fallback)
    {
        return double.IsFinite(value) && value is >= 0.05 and <= 0.90 ? value : fallback;
    }

    private static double ClampPixels(double value, double fallback)
    {
        return double.IsFinite(value) && value is >= 96 and <= 4096 ? value : fallback;
    }
}
