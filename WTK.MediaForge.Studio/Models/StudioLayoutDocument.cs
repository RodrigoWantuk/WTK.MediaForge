using System.Text.Json.Serialization;

namespace WTK.MediaForge.Studio.Models;

public sealed class StudioLayoutDocument
{
    public string Language { get; set; } = "pt-BR";

    public StudioLayoutState Layout { get; set; } = new();
}

public sealed class StudioLayoutState
{
    public double LeftProportion { get; set; } = 0.20;

    public double RightProportion { get; set; } = 0.25;

    public double ProductionProportion { get; set; } = 0.36;

    public double PropertiesProportion { get; set; } = 0.64;

    public double BottomProportion { get; set; } = 0.28;

    public double LeftWidth { get; set; } = 280;

    public double RightWidth { get; set; } = 360;

    public double BottomHeight { get; set; } = 240;

    public Dictionary<string, StudioPanelLayoutState> Panels { get; set; } = StudioPanelLayoutState.CreateDefaults();

    public List<StudioFloatingDockState> FloatingDocks { get; set; } = [];
}

public sealed class StudioFloatingDockState
{
    public string ToolId { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 520;
    public string MonitorId { get; set; } = string.Empty;
}

public sealed class StudioPanelLayoutState
{
    public bool Visible { get; set; } = true;

    public bool Collapsed { get; set; }

    [JsonIgnore]
    public bool IsDockedVisible => Visible;

    public static Dictionary<string, StudioPanelLayoutState> CreateDefaults()
    {
        return new Dictionary<string, StudioPanelLayoutState>(StringComparer.OrdinalIgnoreCase)
        {
            ["navigation"] = new() { Visible = true },
            ["production"] = new() { Visible = true },
            ["properties"] = new() { Visible = true },
            ["layers"] = new() { Visible = true },
            ["sceneOutputs"] = new() { Visible = true },
            ["diagnostics"] = new() { Visible = false },
            ["performance"] = new() { Visible = false },
            ["outputMonitor"] = new() { Visible = false },
            ["audioMixer"] = new() { Visible = false }
        };
    }
}
