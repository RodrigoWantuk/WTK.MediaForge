using Avalonia;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Localization;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.Views.Preview;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioShellViewModelTests
{
    [Fact]
    public void Project_explorer_shows_only_scene_cards()
    {
        var shell = CreateShell();

        var group = Assert.Single(shell.ProjectExplorer.Groups);
        Assert.Equal("Cenas", group.Title);
        Assert.Contains(group.Items, item => item.Name == "Cena principal");
        Assert.DoesNotContain(shell.ProjectExplorer.Groups.SelectMany(item => item.Items), item => item.Kind != StudioProjectItemKind.Scene);
        Assert.Equal(new[] { StudioBottomTabKind.Layers, StudioBottomTabKind.SceneOutputs }, shell.BottomWorkbench.Tabs.Select(tab => tab.Kind));
    }

    [Fact]
    public void Selecting_scene_rebuilds_scene_scoped_layers_and_preview()
    {
        var shell = CreateShell();
        var interview = FindItem(shell, "Interview");

        shell.SelectProjectItem(interview);

        Assert.Equal("Interview", shell.CurrentScene?.DisplayName);
        Assert.Equal("Interview", shell.Preview.SceneName);
        Assert.All(shell.BottomWorkbench.Layers, layer => Assert.StartsWith("layer-interview", layer.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(shell.BottomWorkbench.Layers, layer => layer.Id == "layer-lower-third");
        Assert.Null(shell.SelectedLayer);
        Assert.IsType<SceneInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Add_source_dialog_adds_layer_only_to_current_scene()
    {
        var shell = CreateShell();
        var main = shell.Document.Scenes.Single(scene => scene.Id == "scene-main");
        var interview = FindItem(shell, "Interview");

        shell.SelectProjectItem(interview);
        var originalMainCount = main.Layers.Count;
        var originalInterviewCount = shell.CurrentScene!.Layers.Count;

        shell.AddSourceCommand.Execute(null);
        Assert.True(shell.Dialog.IsOpen);
        var imageOption = shell.Dialog.Options.Single(option => option.Id == "source.image");
        imageOption.SelectCommand!.Execute(null);

        Assert.False(shell.Dialog.IsOpen);
        Assert.Equal(originalMainCount, main.Layers.Count);
        Assert.Equal(originalInterviewCount + 1, shell.CurrentScene!.Layers.Count);
        Assert.Equal(shell.SelectedLayer, shell.Preview.SelectedLayer);
    }

    [Fact]
    public void Selecting_layer_updates_inspector_and_preview_selection()
    {
        var shell = CreateShell();
        var webcam = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");

        shell.SelectLayer(webcam);

        Assert.Equal(webcam, shell.SelectedLayer);
        Assert.Equal(webcam, shell.Preview.SelectedLayer);
        Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Selecting_scene_clears_layer_effect_context()
    {
        var shell = CreateShell();
        var webcam = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");
        shell.SelectLayer(webcam);

        shell.SelectProjectItem(FindItem(shell, "Break BRB"));

        Assert.Null(shell.SelectedLayer);
        Assert.Null(shell.Preview.SelectedLayer);
        Assert.IsType<SceneInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Send_scene_to_output_updates_route_transition_and_panels()
    {
        var shell = CreateShell();

        shell.SendSceneToOutput("output-rtmp-twitch", "scene-interview", "transition-fade", 300);

        var output = shell.Document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        Assert.Equal("scene-interview", output.AssignedSceneId);
        Assert.Equal("transition-fade", output.DefaultTransitionId);
        Assert.Equal(300, output.TransitionDurationMs);
        Assert.Contains(shell.Production.Outputs, item => item.Name == "RTMP Twitch" && item.SceneName == "Interview" && item.TransitionText == "Fade 300 ms");
    }

    [Fact]
    public async Task Stream_and_record_commands_depend_on_configured_outputs_not_engine_state()
    {
        var shell = CreateShell();

        Assert.True(shell.ToggleStreamingCommand.CanExecute(null));
        Assert.True(shell.ToggleRecordingCommand.CanExecute(null));

        await shell.ToggleStreamingCommand.ExecuteAsync(null);
        await shell.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.True(shell.IsStreaming);
        Assert.True(shell.IsRecording);
        Assert.Equal("● Ao vivo", shell.Toolbar.StreamButtonText);
        Assert.StartsWith("● Gravando", shell.Toolbar.RecordingButtonText, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_stream_key_is_masked_in_properties()
    {
        var shell = CreateShell();

        shell.SendSceneToOutput("output-rtmp-twitch", "scene-main", "transition-cut", 0);

        var inspector = Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal("sk_live_************", inspector.MaskedStreamKey);
        Assert.DoesNotContain("2d97c8a6", inspector.MaskedStreamKey, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_secret", inspector.MaskedStreamKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_view_model_supports_move_resize_nudge_and_lock()
    {
        var shell = CreateShell();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Logo.png");

        shell.Preview.SetViewport(1280, 720);
        Assert.Equal("Fit", shell.Preview.ZoomLabel);

        shell.SelectLayer(layer);
        shell.Preview.PanBy(20, 30);
        shell.Preview.MoveLayer(layer, 30, -20, constrainAxis: false);
        shell.Preview.ResizeLayer(layer, ResizeHandleKind.BottomRight, 24, 16, keepAspect: false, fromCenter: false);
        shell.Preview.NudgeSelectedLayer(1, 0, largeStep: true);

        Assert.Equal(1704, layer.X);
        Assert.Equal(906, layer.Y);
        Assert.Equal(200, layer.Width);
        Assert.Equal(120, layer.Height);

        layer.IsLocked = true;
        shell.Preview.MoveLayer(layer, 100, 100, constrainAxis: false);
        Assert.Equal(1704, layer.X);
    }

    [Fact]
    public void Scene_viewport_fit_centers_scene()
    {
        var viewport = new SceneViewportState
        {
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            ViewportWidth = 1280,
            ViewportHeight = 720
        };

        viewport.Fit(48);

        Assert.InRange(viewport.Zoom, 0.57, 0.58);
        Assert.True(Math.Abs((1280 - 1920 * viewport.Zoom) / 2 - viewport.OffsetX) < 0.001);
        Assert.True(Math.Abs((720 - 1080 * viewport.Zoom) / 2 - viewport.OffsetY) < 0.001);
    }

    [Fact]
    public void Scene_viewport_zoom_at_preserves_scene_point_under_cursor()
    {
        var viewport = new SceneViewportState
        {
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            ViewportWidth = 1600,
            ViewportHeight = 900
        };
        viewport.Fit(36);
        var cursor = new Point(640, 360);
        var before = viewport.ScreenToScene(cursor);

        viewport.ZoomAt(cursor, 1.5);

        var after = viewport.ScreenToScene(cursor);
        Assert.True(Math.Abs(before.X - after.X) < 0.001);
        Assert.True(Math.Abs(before.Y - after.Y) < 0.001);
    }

    [Fact]
    public void Scene_viewport_pan_uses_screen_delta()
    {
        var viewport = new SceneViewportState();
        viewport.Pan(new Vector(40, -12));

        Assert.Equal(40, viewport.OffsetX);
        Assert.Equal(-12, viewport.OffsetY);
    }

    [Fact]
    public void Status_and_toolbar_do_not_expose_engine_or_gpu_idle_concepts()
    {
        var shell = CreateShell();
        var visibleText = string.Join(
            " ",
            shell.Toolbar.StateBadge,
            shell.Toolbar.StreamButtonText,
            shell.Toolbar.RecordingButtonText,
            shell.StatusBar.StatusText,
            shell.StatusBar.SceneText,
            shell.StatusBar.OutputText,
            shell.StatusBar.FramesText);

        Assert.DoesNotContain("Engine", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU idle", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preview idle", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start Engine", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mock", visibleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PtBrResources_ShouldContainExpectedAccents()
    {
        var loc = LocalizationManager.Instance;
        var values = new[]
        {
            loc["Term_Settings"],
            loc["Term_Outputs"],
            loc["Term_Transmission"],
            loc["Term_Recording"],
            loc["Term_Diagnostics"],
            loc["Term_Preview"],
            loc["Term_SafeArea"]
        };

        Assert.Equal("pt-BR", loc.CurrentCulture.Name);
        Assert.Contains("Configurações", values);
        Assert.Contains("Saídas", values);
        Assert.Contains("Transmissão", values);
        Assert.Contains("Gravação", values);
        Assert.Contains("Diagnósticos", values);
        Assert.Contains("Prévia", values);
        Assert.Contains("Área segura", values);
    }

    private static StudioShellViewModel CreateShell()
    {
        var services = StudioServiceFactory.CreateFake(uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }

    private static ProjectTreeItemViewModel FindItem(StudioShellViewModel shell, string itemName)
    {
        return shell.ProjectExplorer.Groups
            .SelectMany(group => group.Items)
            .Single(item => item.Name == itemName);
    }
}
