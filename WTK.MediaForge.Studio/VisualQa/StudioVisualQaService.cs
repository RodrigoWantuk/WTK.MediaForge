using System.Text;
using Avalonia;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.VisualQa;

public sealed record StudioVisualQaViewport(string Name, double Width, double Height)
{
    public static IReadOnlyList<StudioVisualQaViewport> ProductTargets { get; } =
    [
        new("1366x768", 1366, 768),
        new("1920x1080", 1920, 1080),
        new("2560x1440", 2560, 1440)
    ];
}

public sealed record StudioVisualQaLayoutEstimate(
    double WindowWidth,
    double WindowHeight,
    double NavigationWidth,
    double RightRailWidth,
    double BottomWorkbenchHeight,
    double EditorWidth,
    double EditorHeight)
{
    public double EditorAreaRatio => WindowWidth <= 0 || WindowHeight <= 0
        ? 0
        : EditorWidth * EditorHeight / (WindowWidth * WindowHeight);
}

public enum StudioVisualQaSeverity
{
    Warning,
    Error
}

public sealed record StudioVisualQaFinding(
    StudioVisualQaSeverity Severity,
    string CheckId,
    string Message);

public sealed record StudioVisualQaViewportResult(
    StudioVisualQaViewport Viewport,
    StudioVisualQaLayoutEstimate Layout,
    Rect CanvasViewportRect,
    IReadOnlyList<StudioVisualQaFinding> Findings)
{
    public bool Passed => Findings.All(finding => finding.Severity != StudioVisualQaSeverity.Error);
}

public sealed class StudioVisualQaReport
{
    public StudioVisualQaReport(
        IReadOnlyList<StudioVisualQaViewportResult> viewports,
        IReadOnlyList<StudioVisualQaFinding> globalFindings)
    {
        Viewports = viewports;
        GlobalFindings = globalFindings;
    }

    public IReadOnlyList<StudioVisualQaViewportResult> Viewports { get; }

    public IReadOnlyList<StudioVisualQaFinding> GlobalFindings { get; }

    public bool Passed =>
        GlobalFindings.All(finding => finding.Severity != StudioVisualQaSeverity.Error)
        && Viewports.All(viewport => viewport.Passed);

    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Studio UI Visual QA Report");
        builder.AppendLine();
        builder.AppendLine($"Status: {(Passed ? "Passed" : "Failed")}");
        builder.AppendLine();
        builder.AppendLine("## Global Checks");
        AppendFindings(builder, GlobalFindings);

        foreach (var result in Viewports)
        {
            builder.AppendLine();
            builder.AppendLine($"## {result.Viewport.Name}");
            builder.AppendLine(
                $"- Editor estimate: {result.Layout.EditorWidth:0}x{result.Layout.EditorHeight:0} ({result.Layout.EditorAreaRatio:P0} of window)");
            builder.AppendLine(
                $"- Canvas fit: {result.CanvasViewportRect.Width:0}x{result.CanvasViewportRect.Height:0} at ({result.CanvasViewportRect.X:0}, {result.CanvasViewportRect.Y:0})");
            AppendFindings(builder, result.Findings);
        }

        return builder.ToString();
    }

    private static void AppendFindings(StringBuilder builder, IReadOnlyList<StudioVisualQaFinding> findings)
    {
        if (findings.Count == 0)
        {
            builder.AppendLine("- No findings.");
            return;
        }

        foreach (var finding in findings)
        {
            builder.AppendLine($"- {finding.Severity}: `{finding.CheckId}` - {finding.Message}");
        }
    }
}

public sealed class StudioVisualQaService
{
    private const double TitleBarHeight = 36;
    private const double ToolbarHeight = 36;
    private const double StatusBarHeight = 26;
    private const double DockSplitterBudget = 16;
    private const double CenterSplitterBudget = 8;
    private const double MinimumEditorWidth = 420;
    private const double MinimumEditorHeight = 260;
    private const double MinimumCanvasWidth = 360;
    private const double MinimumCanvasHeight = 200;
    private static readonly (string Left, string Right)[] BannedPrimaryPhraseParts =
    [
        ("Start", "Engine"),
        ("Stop", "Engine"),
        ("GPU", "idle"),
        ("Preview", "idle"),
        ("command", "buffer"),
        ("keyed", "mutex"),
        ("native", "handle")
    ];

    private static readonly string[] BannedPrimarySingleTerms =
    [
        "fence",
        "mock"
    ];

    public StudioVisualQaReport Run(
        StudioShellViewModel shell,
        IReadOnlyList<StudioVisualQaViewport>? viewports = null)
    {
        ArgumentNullException.ThrowIfNull(shell);

        viewports ??= StudioVisualQaViewport.ProductTargets;
        var globalFindings = ValidateGlobalShellContract(shell).ToArray();
        var results = viewports
            .Select(viewport => ValidateViewport(shell, viewport))
            .ToArray();

        return new StudioVisualQaReport(results, globalFindings);
    }

    public StudioVisualQaLayoutEstimate EstimateLayout(
        StudioShellViewModel shell,
        StudioVisualQaViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(viewport);

        var workspaceHeight = Math.Max(1, viewport.Height - TitleBarHeight - ToolbarHeight - StatusBarHeight);
        var navigationWidth = shell.NavigationDock.IsContentVisible
            ? Math.Clamp(viewport.Width * shell.NavigationLayoutProportion, 240, 340)
            : 0;
        var rightRailWidth = shell.ProductionDock.IsContentVisible || shell.PropertiesDock.IsContentVisible
            ? Math.Clamp(viewport.Width * shell.RightLayoutProportion, 340, 520)
            : 0;
        var bottomHeight = shell.WorkbenchDock.IsContentVisible
            ? Math.Clamp(workspaceHeight * shell.WorkbenchLayoutProportion, 180, 280)
            : 0;
        var editorWidth = Math.Max(1, viewport.Width - navigationWidth - rightRailWidth - DockSplitterBudget);
        var editorHeight = Math.Max(1, workspaceHeight - bottomHeight - CenterSplitterBudget);

        return new StudioVisualQaLayoutEstimate(
            viewport.Width,
            viewport.Height,
            navigationWidth,
            rightRailWidth,
            bottomHeight,
            editorWidth,
            editorHeight);
    }

    private StudioVisualQaViewportResult ValidateViewport(StudioShellViewModel shell, StudioVisualQaViewport viewport)
    {
        var layout = EstimateLayout(shell, viewport);
        shell.Preview.SetViewport(layout.EditorWidth, layout.EditorHeight);
        shell.Preview.FitZoom();

        var canvas = shell.Preview.Transform.SceneToViewport(new Rect(
            0,
            0,
            shell.Preview.CanvasWidth,
            shell.Preview.CanvasHeight));
        var findings = ValidateViewportContract(shell, layout, canvas).ToArray();

        return new StudioVisualQaViewportResult(viewport, layout, canvas, findings);
    }

    private static IEnumerable<StudioVisualQaFinding> ValidateGlobalShellContract(StudioShellViewModel shell)
    {
        if (shell.ProjectExplorer.Groups.Count != 1 || shell.ProjectExplorer.Groups[0].Title != "Cenas")
        {
            yield return Error("project.navigation", "The primary navigation tree must contain only the scene group.");
        }

        if (shell.ProjectExplorer.Scenes.Count == 0)
        {
            yield return Error("project.scenes", "At least one scene card must be available.");
        }

        if (shell.ProjectExplorer.Sources.Count == 0)
        {
            yield return Error("project.sources", "The source library tab must expose reusable sources.");
        }

        if (shell.ProjectExplorer.Outputs.Count == 0)
        {
            yield return Error("project.outputs", "The output tab must expose configured and planned sinks.");
        }

        var expectedTabs = new[] { StudioBottomTabKind.Layers, StudioBottomTabKind.SceneOutputs };
        var actualTabs = shell.BottomWorkbench.Tabs.Select(tab => tab.Kind).ToArray();
        if (!actualTabs.SequenceEqual(expectedTabs))
        {
            yield return Error("bottom.tabs", "The main bottom workbench must contain only Camadas and Saídas da cena.");
        }

        if (shell.BottomWorkbench.Layers.Count == 0 || shell.Preview.Layers.Count == 0)
        {
            yield return Error("scene.layers", "The current scene must expose editable layers in the canvas and layer list.");
        }

        if (shell.Production.Outputs.Count == 0)
        {
            yield return Error("production.outputs", "Production output cards must remain visible in the right rail.");
        }

        if (shell.Inspector.SelectedPage is null or EmptyInspectorViewModel)
        {
            yield return Error("properties.context", "Properties must show the current scene, layer, source, or output context.");
        }

        foreach (var text in EnumerateMainVisibleText(shell))
        {
            if (ContainsBannedPrimaryText(text))
            {
                yield return Error("primary.technical-language", $"Primary UI text exposes low-level or mock language: '{text}'.");
            }
        }
    }

    private static IEnumerable<StudioVisualQaFinding> ValidateViewportContract(
        StudioShellViewModel shell,
        StudioVisualQaLayoutEstimate layout,
        Rect canvas)
    {
        if (layout.EditorWidth < MinimumEditorWidth)
        {
            yield return Error("viewport.editor.width", $"Editor width is too small: {layout.EditorWidth:0}px.");
        }

        if (layout.EditorHeight < MinimumEditorHeight)
        {
            yield return Error("viewport.editor.height", $"Editor height is too small: {layout.EditorHeight:0}px.");
        }

        if (canvas.Width < MinimumCanvasWidth || canvas.Height < MinimumCanvasHeight)
        {
            yield return Error("viewport.canvas.size", $"Fitted canvas is too small: {canvas.Width:0}x{canvas.Height:0}px.");
        }

        if (canvas.Left < -0.5 || canvas.Top < -0.5 || canvas.Right > layout.EditorWidth + 0.5 || canvas.Bottom > layout.EditorHeight + 0.5)
        {
            yield return Error("viewport.canvas.bounds", "Fitted canvas must remain fully inside the editor viewport.");
        }

        var canvasCenterX = canvas.X + canvas.Width / 2;
        var canvasCenterY = canvas.Y + canvas.Height / 2;
        if (Math.Abs(canvasCenterX - layout.EditorWidth / 2) > 1 || Math.Abs(canvasCenterY - layout.EditorHeight / 2) > 1)
        {
            yield return Error("viewport.canvas.center", "Fitted canvas must remain centered instead of drifting to a corner.");
        }

        if (canvas.Width < layout.EditorWidth * 0.55 || canvas.Height < layout.EditorHeight * 0.55)
        {
            yield return Warning("viewport.canvas.presence", "Fitted canvas uses less visual space than expected for the dominant editor surface.");
        }

        if (!shell.Preview.IsFitZoom || shell.Preview.Zoom <= 0)
        {
            yield return Error("viewport.zoom.state", "Visual QA requires a valid fit zoom state.");
        }
    }

    private static IEnumerable<string> EnumerateMainVisibleText(StudioShellViewModel shell)
    {
        yield return shell.TitleBar.ProductName;
        yield return shell.TitleBar.ProjectName;
        yield return shell.TitleBar.WorkspaceState;
        yield return shell.Toolbar.StateBadge;
        yield return shell.Toolbar.StreamButtonText;
        yield return shell.Toolbar.RecordingButtonText;
        yield return shell.ProjectExplorer.TabTitle;
        yield return shell.ProjectExplorer.SearchPlaceholder;
        yield return shell.ProjectExplorer.AddButtonText;
        yield return shell.Inspector.SelectedPage?.Title ?? string.Empty;
        yield return shell.Inspector.SelectedPage?.Subtitle ?? string.Empty;
        yield return shell.StatusBar.StatusText;
        yield return shell.StatusBar.CenterText;
        yield return shell.StatusBar.RightText;

        foreach (var group in shell.ProjectExplorer.Groups)
        {
            yield return group.Title;
            foreach (var item in group.VisibleItems)
            {
                yield return item.Name;
                yield return item.Metadata;
                yield return item.Badge;
                yield return item.Detail;
            }
        }

        foreach (var tab in shell.BottomWorkbench.Tabs)
        {
            yield return tab.Title;
        }

        foreach (var output in shell.Production.Outputs)
        {
            yield return output.Name;
            yield return output.SceneName;
            yield return output.StatusText;
            yield return output.TransitionText;
            yield return output.RouteButtonText;
        }
    }

    private static bool ContainsBannedPrimaryText(string text)
    {
        foreach (var (left, right) in BannedPrimaryPhraseParts)
        {
            if (text.Contains($"{left} {right}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return BannedPrimarySingleTerms.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static StudioVisualQaFinding Error(string checkId, string message)
    {
        return new StudioVisualQaFinding(StudioVisualQaSeverity.Error, checkId, message);
    }

    private static StudioVisualQaFinding Warning(string checkId, string message)
    {
        return new StudioVisualQaFinding(StudioVisualQaSeverity.Warning, checkId, message);
    }
}
