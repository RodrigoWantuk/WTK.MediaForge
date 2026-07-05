using Avalonia;
using Avalonia.Input;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.Views.Preview;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioShellViewModelTests
{
    [Fact]
    public void Left_navigation_uses_tabs_for_scenes_sources_and_outputs()
    {
        var shell = CreateShell();

        Assert.Equal(StudioExplorerTabKind.Scenes, shell.ProjectExplorer.SelectedTab);
        Assert.Contains(shell.ProjectExplorer.Scenes, item => item.Name == "Cena principal");
        Assert.Contains(shell.ProjectExplorer.Sources, item => item.Name == "Webcam");
        Assert.Contains(shell.ProjectExplorer.Outputs, item => item.Name == "RTMP Twitch");
        Assert.Equal(new[] { StudioBottomTabKind.Layers, StudioBottomTabKind.SceneOutputs }, shell.BottomWorkbench.Tabs.Select(tab => tab.Kind));
    }

    [Fact]
    public void SceneSelectionUpdatesWorkspace()
    {
        var shell = CreateShell();

        shell.SelectSceneCard(shell.ProjectExplorer.Scenes.Single(scene => scene.Name == "Interview"));

        Assert.Equal("Interview", shell.CurrentScene?.DisplayName);
        Assert.Equal("Interview", shell.Preview.SceneName);
        Assert.All(shell.BottomWorkbench.Layers, layer => Assert.StartsWith("layer-interview", layer.Id, StringComparison.Ordinal));
        Assert.Null(shell.SelectedLayer);
        Assert.IsType<SceneInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Selecting_source_shows_source_properties_without_rebuilding_scene_layers()
    {
        var shell = CreateShell();
        var originalLayerIds = shell.BottomWorkbench.Layers.Select(layer => layer.Id).ToArray();

        shell.SelectSourceCard(shell.ProjectExplorer.Sources.Single(source => source.Name == "Logo.png"));

        Assert.Equal(originalLayerIds, shell.BottomWorkbench.Layers.Select(layer => layer.Id));
        Assert.IsType<SourceInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Null(shell.SelectedLayer);
    }

    [Fact]
    public void Add_source_dialog_adds_layer_only_to_current_scene()
    {
        var shell = CreateShell();
        var main = shell.Document.Scenes.Single(scene => scene.Id == "scene-main");
        shell.SelectSceneCard(shell.ProjectExplorer.Scenes.Single(scene => scene.Name == "Interview"));
        var originalMainCount = main.Layers.Count;
        var originalInterviewCount = shell.CurrentScene!.Layers.Count;

        shell.AddSourceCommand.Execute(null);
        shell.Dialog.Options.Single(option => option.Id == "source.image").SelectCommand!.Execute(null);

        Assert.Equal(originalMainCount, main.Layers.Count);
        Assert.Equal(originalInterviewCount + 1, shell.CurrentScene!.Layers.Count);
        Assert.Equal(shell.SelectedLayer, shell.Preview.SelectedLayer);
    }

    [Fact]
    public void LayerSelectionSynchronizesPreviewLayersAndInspector()
    {
        var shell = CreateShell();
        var webcam = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");

        shell.SelectLayer(webcam);

        Assert.Equal(webcam, shell.SelectedLayer);
        Assert.Equal(webcam, shell.Preview.SelectedLayer);
        Assert.Equal(webcam, shell.BottomWorkbench.SelectedLayer);
        Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);

        shell.Preview.RequestLayerSelection(shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Logo.png"));

        Assert.Equal("Logo.png", shell.SelectedLayer?.Name);
        Assert.Equal(shell.SelectedLayer, shell.BottomWorkbench.SelectedLayer);
        Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Selecting_scene_clears_layer_effect_context()
    {
        var shell = CreateShell();
        shell.SelectLayer(shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam"));

        shell.SelectSceneCard(shell.ProjectExplorer.Scenes.Single(scene => scene.Name == "Break BRB"));

        Assert.Null(shell.SelectedLayer);
        Assert.Null(shell.Preview.SelectedLayer);
        Assert.IsType<SceneInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void OutputRoutingTests()
    {
        var shell = CreateShell();
        var output = shell.Document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        output.IsLive = true;

        shell.SendSceneToOutput("output-rtmp-twitch", "scene-brb", "transition-fade", 300);

        Assert.Contains(shell.Production.Outputs, item => item.Name == "RTMP Twitch" && item.SceneName == "Break BRB" && item.TransitionText == "Fade 300 ms");
        var inspector = Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal("Fade", inspector.TransitionOptions.Single(item => item.Id == "transition-fade").Name);

        shell.Production.Outputs.Single(item => item.Id == "output-rtmp-twitch").SendSceneCommand!.Execute(null);
        Assert.True(shell.Dialog.RequiresLiveConfirmation);
    }

    [Fact]
    public async Task Stream_and_record_commands_depend_on_configured_outputs_not_engine_state()
    {
        var shell = CreateShell();

        await shell.ToggleStreamingCommand.ExecuteAsync(null);
        await shell.ToggleRecordingCommand.ExecuteAsync(null);

        Assert.True(shell.IsStreaming);
        Assert.True(shell.IsRecording);
        Assert.Equal("Ao vivo", shell.Toolbar.StreamButtonText);
        Assert.StartsWith("Gravando", shell.Toolbar.RecordingButtonText, StringComparison.Ordinal);
        Assert.Contains("Ao vivo", shell.StatusBar.RightText);
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
    public void Status_and_toolbar_do_not_expose_engine_or_gpu_idle_concepts()
    {
        var shell = CreateShell();
        var visibleText = string.Join(
            " ",
            shell.Toolbar.StateBadge,
            shell.Toolbar.StreamButtonText,
            shell.Toolbar.RecordingButtonText,
            shell.StatusBar.StatusText,
            shell.StatusBar.CenterText,
            shell.StatusBar.RightText);

        Assert.DoesNotContain("Engine", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPU idle", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preview idle", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start Engine", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mock", visibleText, StringComparison.OrdinalIgnoreCase);
    }

    private static StudioShellViewModel CreateShell()
    {
        var services = StudioServiceFactory.CreateFake(uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }
}

public sealed class SceneEditorTransformTests
{
    [Fact]
    public void Fit_centers_scene()
    {
        var transform = new SceneEditorTransform
        {
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            ViewportWidth = 1280,
            ViewportHeight = 720
        };

        transform.Fit(48);

        Assert.InRange(transform.Zoom, 0.57, 0.58);
        Assert.True(Math.Abs((1280 - 1920 * transform.Zoom) / 2 - transform.PanX) < 0.001);
        Assert.True(Math.Abs((720 - 1080 * transform.Zoom) / 2 - transform.PanY) < 0.001);
    }

    [Fact]
    public void Zoom_at_cursor_preserves_scene_point()
    {
        var transform = new SceneEditorTransform
        {
            CanvasWidth = 1920,
            CanvasHeight = 1080,
            ViewportWidth = 1600,
            ViewportHeight = 900
        };
        transform.Fit(36);
        var cursor = new Point(640, 360);
        var before = transform.ViewportToScene(cursor);

        transform.ZoomAt(cursor, 1.5);

        var after = transform.ViewportToScene(cursor);
        Assert.True(Math.Abs(before.X - after.X) < 0.001);
        Assert.True(Math.Abs(before.Y - after.Y) < 0.001);
    }

    [Fact]
    public void Scene_and_viewport_coordinates_are_inverse()
    {
        var transform = new SceneEditorTransform { CanvasWidth = 1920, CanvasHeight = 1080, ViewportWidth = 1200, ViewportHeight = 800 };
        transform.Fit(24);
        transform.PanBy(new Vector(17, -9));
        var scene = new Point(512, 320);

        var roundtrip = transform.ViewportToScene(transform.SceneToViewport(scene));

        Assert.True(Math.Abs(scene.X - roundtrip.X) < 0.001);
        Assert.True(Math.Abs(scene.Y - roundtrip.Y) < 0.001);
    }

    [Fact]
    public void Pan_offsets_viewport_coordinates()
    {
        var transform = new SceneEditorTransform();
        transform.PanBy(new Vector(40, -12));

        Assert.Equal(40, transform.PanX);
        Assert.Equal(-12, transform.PanY);
    }
}

public sealed class PreviewHitTestTests
{
    [Fact]
    public void Hit_test_selects_top_visible_layer_at_multiple_zooms()
    {
        var preview = new PreviewCanvasViewModel();
        preview.SetViewport(1280, 720);
        var bottom = CreateLayer("Bottom", 100, 100, 300, 200, order: 1);
        var top = CreateLayer("Top", 140, 130, 240, 160, order: 2);
        preview.Layers.Add(bottom);
        preview.Layers.Add(top);

        foreach (var zoom in new[] { 0.18, 0.38, 0.51, 1, 2 })
        {
            preview.Transform.SetZoomAt(preview.ViewportCenter, zoom);
            var viewport = preview.Transform.SceneToViewport(new Point(180, 160));
            var scene = preview.ScreenToScene(viewport);

            Assert.Equal("Top", preview.HitTest(scene)?.Name);
        }
    }

    [Fact]
    public void Invisible_layer_is_ignored_and_locked_layer_is_selectable_but_immovable()
    {
        var preview = new PreviewCanvasViewModel();
        preview.SetCanvas(1920, 1080, 60, true);
        var invisible = CreateLayer("Invisible", 100, 100, 300, 200, order: 3);
        invisible.IsVisible = false;
        var locked = CreateLayer("Locked", 120, 120, 300, 200, order: 2);
        locked.IsLocked = true;
        preview.Layers.Add(invisible);
        preview.Layers.Add(locked);

        Assert.Equal("Locked", preview.HitTest(new Point(150, 150))?.Name);
        preview.MoveLayerFromStart(locked, locked.X, locked.Y, new Vector(80, 80), KeyModifiers.None);

        Assert.Equal(120, locked.X);
        Assert.Equal(120, locked.Y);
    }

    private static LayerItemViewModel CreateLayer(string name, double x, double y, double width, double height, int order)
    {
        var layer = new LayerItemViewModel(name, "Test Source", "Source", StudioIconKind.Source, order);
        layer.X = x;
        layer.Y = y;
        layer.Width = width;
        layer.Height = height;
        return layer;
    }
}

public sealed class SnapMovementTests
{
    [Fact]
    public void Drag_uses_snap_alt_disables_snap_ctrl_uses_precision_and_shift_locks_axis()
    {
        var preview = new PreviewCanvasViewModel();
        preview.SetCanvas(1920, 1080, 60, true);
        var layer = new LayerItemViewModel("Layer", "Source", "Source", StudioIconKind.Source, 1)
        {
            X = 100,
            Y = 100,
            Width = 300,
            Height = 200
        };
        preview.Layers.Add(layer);

        preview.MoveLayerFromStart(layer, 100, 100, new Vector(13, 17), KeyModifiers.None);
        Assert.Equal(110, layer.X);
        Assert.Equal(120, layer.Y);

        preview.MoveLayerFromStart(layer, 100, 100, new Vector(13, 17), KeyModifiers.Alt);
        Assert.Equal(113, layer.X);
        Assert.Equal(117, layer.Y);

        preview.MoveLayerFromStart(layer, 100, 100, new Vector(13, 17), KeyModifiers.Control);
        Assert.Equal(113, layer.X);
        Assert.Equal(117, layer.Y);

        preview.MoveLayerFromStart(layer, 100, 100, new Vector(45, 12), KeyModifiers.Shift);
        Assert.Equal(150, layer.X);
        Assert.Equal(100, layer.Y);
    }

    [Fact]
    public void Resize_uses_handles_snap_and_min_size()
    {
        var preview = new PreviewCanvasViewModel();
        preview.SetCanvas(1920, 1080, 60, true);
        var layer = new LayerItemViewModel("Layer", "Source", "Source", StudioIconKind.Source, 1)
        {
            X = 100,
            Y = 100,
            Width = 300,
            Height = 200
        };

        preview.ResizeLayerFromStart(layer, ResizeHandleKind.BottomRight, new Rect(100, 100, 300, 200), new Vector(13, 17), KeyModifiers.None);

        Assert.Equal(310, layer.Width);
        Assert.Equal(220, layer.Height);
    }
}
