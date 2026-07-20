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
}

public sealed class StudioPanelLayoutState
{
    public bool Visible { get; set; } = true;

    public bool Floating { get; set; }

    public bool Collapsed { get; set; }

    public double FloatingX { get; set; } = 120;

    public double FloatingY { get; set; } = 120;

    public double FloatingWidth { get; set; } = 420;

    public double FloatingHeight { get; set; } = 520;

    [JsonIgnore]
    public bool IsDockedVisible => Visible && !Floating;

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
