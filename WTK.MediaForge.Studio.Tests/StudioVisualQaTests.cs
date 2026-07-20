using Avalonia;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.VisualQa;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioVisualQaTests
{
    [Fact]
    public void Product_viewports_pass_visual_contract()
    {
        var shell = CreateShell();
        var report = new StudioVisualQaService().Run(shell);

        Assert.True(report.Passed, report.ToMarkdown());
        Assert.Equal(
            ["1366x768", "1600x900", "1920x1080"],
            report.Viewports.Select(result => result.Viewport.Name).ToArray());
    }

    [Fact]
    public void Visual_qa_report_documents_viewport_evidence()
    {
        var shell = CreateShell();
        var report = new StudioVisualQaService().Run(shell);

        var markdown = report.ToMarkdown();

        Assert.Contains("# Studio UI Visual QA Report", markdown, StringComparison.Ordinal);
        Assert.Contains("1366x768", markdown, StringComparison.Ordinal);
        Assert.Contains("1600x900", markdown, StringComparison.Ordinal);
        Assert.Contains("1920x1080", markdown, StringComparison.Ordinal);
        Assert.Contains("Canvas fit", markdown, StringComparison.Ordinal);
        Assert.Contains("Status: Passed", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Visual_qa_rejects_return_of_advanced_tabs_to_main_workbench()
    {
        var shell = CreateShell();
        shell.BottomWorkbench.Tabs.Add(new BottomTabViewModel((StudioBottomTabKind)99, "Diagnosticos"));

        var report = new StudioVisualQaService().Run(shell);

        Assert.False(report.Passed);
        Assert.Contains(report.GlobalFindings, finding => finding.CheckId == "bottom.tabs");
    }

    [Fact]
    public void Target_viewport_layout_keeps_editor_as_usable_surface()
    {
        var shell = CreateShell();
        var service = new StudioVisualQaService();

        foreach (var viewport in StudioVisualQaViewport.ProductTargets)
        {
            var layout = service.EstimateLayout(shell, viewport);

            Assert.True(layout.EditorWidth >= 420, $"{viewport.Name} editor width was {layout.EditorWidth:0}px.");
            Assert.True(layout.EditorHeight >= 260, $"{viewport.Name} editor height was {layout.EditorHeight:0}px.");
            Assert.InRange(layout.EditorAreaRatio, 0.25, 0.70);
        }
    }

    [Fact]
    public void Fit_zoom_remains_centered_after_target_viewport_changes()
    {
        var shell = CreateShell();

        foreach (var viewport in StudioVisualQaViewport.ProductTargets)
        {
            var layout = new StudioVisualQaService().EstimateLayout(shell, viewport);
            shell.Preview.SetViewport(layout.EditorWidth, layout.EditorHeight);
            shell.Preview.FitZoom();
            var center = shell.Preview.Transform.SceneToViewport(new Point(shell.Preview.CanvasWidth / 2, shell.Preview.CanvasHeight / 2));

            Assert.True(Math.Abs(center.X - layout.EditorWidth / 2) <= 1, $"{viewport.Name} canvas center X drifted.");
            Assert.True(Math.Abs(center.Y - layout.EditorHeight / 2) <= 1, $"{viewport.Name} canvas center Y drifted.");
        }
    }

    private static StudioShellViewModel CreateShell()
    {
        var services = StudioServiceFactory.CreateFake(uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }
}
