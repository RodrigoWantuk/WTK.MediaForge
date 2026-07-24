using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioAccessibilityTests
{
    [Theory]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/StudioToolbarView.axaml",
        "toolbar.new-project",
        "toolbar.open-project",
        "toolbar.save-project",
        "toolbar.undo",
        "toolbar.redo",
        "toolbar.stream",
        "toolbar.record",
        "toolbar.settings")]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/ProjectExplorerView.axaml",
        "project-explorer",
        "project-explorer.tab.scenes",
        "project-explorer.tab.sources",
        "project-explorer.tab.outputs",
        "project-explorer.search",
        "project-explorer.add-current")]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/ProductionPanelView.axaml",
        "production.outputs")]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/BottomWorkbenchView.axaml",
        "bottom-workbench")]
    [InlineData("WTK.MediaForge.Studio/Views/Preview/StudioCanvasEditor.axaml",
        "preview.canvas-editor")]
    public void Primary_interactive_views_have_stable_automation_ids(string relativePath, params string[] expectedIds)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("AutomationProperties.Name", text, StringComparison.Ordinal);
        foreach (var id in expectedIds)
        {
            Assert.Contains(id, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/StudioToolbarView.axaml", "HelpText")]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/ProjectExplorerView.axaml", "HelpText")]
    [InlineData("WTK.MediaForge.Studio/Views/Shell/ProductionPanelView.axaml", "HelpText")]
    [InlineData("WTK.MediaForge.Studio/Views/Preview/StudioCanvasEditor.axaml", "HelpText")]
    public void Primary_interactive_views_explain_non_obvious_actions(string relativePath, string expectedFragment)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains(expectedFragment, text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "WTK.MediaForge.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
