using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Localization;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioShellViewModelTests
{
    [Fact]
    public void Design_data_contains_required_groups_and_items()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Scenes" && group.Items.Any(item => item.Name == "Main Scene"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Sources" && group.Items.Any(item => item.Name == "Webcam"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Outputs" && group.Items.Any(item => item.Name == "RTMP Twitch"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Presets" && group.Items.Any(item => item.Name == "YouTube 1080p60"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Packages" && group.Items.Any(item => item.Name == "Brand Kit"));
        Assert.Contains(shell.BottomWorkbench.Layers, layer => layer.Name == "Lower Third");
        Assert.Contains(shell.BottomWorkbench.Effects, effect => effect.Name == "Chroma Key" && effect.IsEnabled);
    }

    [Fact]
    public void Selecting_project_item_updates_selected_item_and_inspector()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var webcam = FindItem(shell, "Webcam");

        shell.SelectProjectItem(webcam);

        Assert.Same(webcam, shell.SelectedProjectItem);
        Assert.Null(shell.SelectedLayer);
        Assert.True(webcam.IsSelected);
        Assert.IsType<SourceInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal(StudioSelectionKind.Source, shell.CurrentSelection.Kind);
        Assert.Equal("Selected Webcam", shell.StatusBar.StatusText);
    }

    [Fact]
    public async Task Toggle_engine_command_updates_toolbar_and_status()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        Assert.False(shell.IsEngineRunning);
        Assert.False(shell.ToggleStreamingCommand.CanExecute(null));

        await shell.ToggleEngineCommand.ExecuteAsync(null);

        Assert.True(shell.IsEngineRunning);
        Assert.Equal("Stop Engine", shell.Toolbar.EngineButtonText);
        Assert.Equal("Running", shell.StatusBar.EngineText);
        Assert.True(shell.ToggleStreamingCommand.CanExecute(null));

        await shell.ToggleEngineCommand.ExecuteAsync(null);

        Assert.False(shell.IsEngineRunning);
        Assert.Equal("Start Engine", shell.Toolbar.EngineButtonText);
        Assert.Equal("Stopped", shell.StatusBar.EngineText);
    }

    [Fact]
    public void Bottom_workbench_tabs_switch_selected_content()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var outputMonitor = shell.BottomWorkbench.Tabs.Single(tab => tab.Kind == StudioBottomTabKind.OutputMonitor);

        shell.BottomWorkbench.SelectTabCommand.Execute(outputMonitor);

        Assert.Same(outputMonitor, shell.BottomWorkbench.SelectedTab);
        Assert.True(shell.BottomWorkbench.IsOutputMonitorSelected);
        Assert.False(shell.BottomWorkbench.IsLayersSelected);
    }

    [Theory]
    [InlineData("Main Scene", typeof(SceneInspectorViewModel))]
    [InlineData("Webcam", typeof(SourceInspectorViewModel))]
    [InlineData("RTMP Twitch", typeof(OutputInspectorViewModel))]
    [InlineData("1080p Streaming", typeof(PresetInspectorViewModel))]
    [InlineData("Brand Kit", typeof(PackageInspectorViewModel))]
    public void Project_item_selection_resolves_contextual_inspector(string itemName, Type inspectorType)
    {
        var shell = StudioDesignData.CreateShellViewModel();

        shell.SelectProjectItem(FindItem(shell, itemName));

        Assert.IsType(inspectorType, shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Selecting_layer_updates_preview_and_layer_inspector()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var webcamLayer = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");

        shell.SelectLayer(webcamLayer);

        Assert.Same(webcamLayer, shell.SelectedLayer);
        Assert.Null(shell.SelectedProjectItem);
        Assert.True(webcamLayer.IsSelected);
        Assert.Equal("Webcam", shell.Preview.SelectedLayerName);
        Assert.Equal(StudioSelectionKind.Layer, shell.CurrentSelection.Kind);
        Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Layer_row_selection_does_not_toggle_visibility()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var logoLayer = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Logo.png");

        shell.SelectLayer(logoLayer);

        Assert.True(logoLayer.IsVisible);
        Assert.True(logoLayer.IsSelected);
    }

    [Fact]
    public void Layer_visibility_command_does_not_change_selection_when_called_directly()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var selectedLayer = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");
        var toggledLayer = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Logo.png");

        shell.SelectLayer(selectedLayer);
        shell.ToggleLayerVisibilityCommand.Execute(toggledLayer);

        Assert.Same(selectedLayer, shell.SelectedLayer);
        Assert.True(selectedLayer.IsSelected);
        Assert.False(toggledLayer.IsSelected);
        Assert.False(toggledLayer.IsVisible);
    }

    [Fact]
    public void Project_item_selection_clears_layer_selection()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Webcam");
        var source = FindItem(shell, "Webcam");

        shell.SelectLayer(layer);
        shell.SelectProjectItem(source);

        Assert.Same(source, shell.SelectedProjectItem);
        Assert.Null(shell.SelectedLayer);
        Assert.False(layer.IsSelected);
    }

    [Fact]
    public void Layer_selection_clears_project_selection()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var source = FindItem(shell, "Webcam");
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Webcam");

        shell.SelectProjectItem(source);
        shell.SelectLayer(layer);

        Assert.Null(shell.SelectedProjectItem);
        Assert.Same(layer, shell.SelectedLayer);
        Assert.False(source.IsSelected);
    }

    [Fact]
    public void Output_stream_key_is_masked_in_inspector()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        shell.SelectProjectItem(FindItem(shell, "RTMP Twitch"));

        var inspector = Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal("sk_live_************", inspector.MaskedStreamKey);
        Assert.DoesNotContain("2d97c8a6", inspector.MaskedStreamKey, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_secret", inspector.MaskedStreamKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commands_respect_can_execute_when_engine_is_stopped()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        Assert.False(shell.ToggleStreamingCommand.CanExecute(null));
        Assert.False(shell.ToggleRecordingCommand.CanExecute(null));

        shell.ToggleStreamingCommand.Execute(null);
        shell.ToggleRecordingCommand.Execute(null);

        Assert.False(shell.IsStreaming);
        Assert.False(shell.IsRecording);

        await shell.ToggleEngineCommand.ExecuteAsync(null);

        Assert.True(shell.ToggleStreamingCommand.CanExecute(null));
        Assert.True(shell.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public void Layer_visibility_and_lock_use_product_glyphs()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Logo.png");

        Assert.Equal("Visible", layer.VisibilityGlyph);
        Assert.Equal("Editable", layer.LockGlyph);

        shell.ToggleLayerVisibilityCommand.Execute(layer);
        shell.ToggleLayerLockCommand.Execute(layer);

        Assert.Equal("Hidden", layer.VisibilityGlyph);
        Assert.Equal("Locked", layer.LockGlyph);
    }

    [Fact]
    public void Layer_inspector_uses_typed_editable_properties()
    {
        var inspector = new LayerInspectorViewModel("Lower Third", "Text");

        inspector.X = 111.5;
        inspector.Opacity = 140;

        Assert.Equal(111.5, inspector.X);
        Assert.Equal(100, inspector.Opacity);
        Assert.Equal(StudioBlendMode.Alpha, inspector.BlendMode);
        Assert.Equal("0 / 0 / 0 / 0", inspector.CropText);
    }

    [Fact]
    public void Selecting_scene_updates_preview_header()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var interview = FindItem(shell, "Interview");

        shell.SelectProjectItem(interview);

        Assert.Equal("Interview", shell.Preview.SceneName);
        Assert.Equal("1920 x 1080", shell.Preview.CanvasSize);
        Assert.Equal("60 fps", shell.Preview.FrameRate);
        Assert.Equal("Fit", shell.Preview.ZoomLabel);
    }

    [Fact]
    public void Preview_canvas_properties_raise_property_changed()
    {
        var preview = new PreviewCanvasViewModel();
        var changed = new List<string?>();
        preview.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        preview.SceneName = "Interview";
        preview.CanvasSize = "1280 x 720";
        preview.FrameRate = "30 fps";
        preview.Zoom = 1;

        Assert.Contains(nameof(PreviewCanvasViewModel.SceneName), changed);
        Assert.Contains(nameof(PreviewCanvasViewModel.CanvasSize), changed);
        Assert.Contains(nameof(PreviewCanvasViewModel.FrameRate), changed);
        Assert.Contains(nameof(PreviewCanvasViewModel.ZoomLabel), changed);
    }

    [Fact]
    public void Layer_inspector_edits_shared_layer_used_by_preview()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var lowerThird = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Lower Third");

        shell.SelectLayer(lowerThird);

        var inspector = Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);
        inspector.X = 320;
        inspector.Y = 700;
        inspector.Width = 900;
        inspector.Height = 120;

        Assert.Equal(320, lowerThird.X);
        Assert.Equal(700, lowerThird.Y);
        Assert.Equal(900, shell.Preview.SelectedLayer!.Width);
        Assert.Equal(120, shell.Preview.SelectedLayer.Height);
    }

    [Fact]
    public void Preview_move_layer_updates_shared_layer()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Logo.png");

        shell.SelectLayer(layer);
        shell.Preview.MoveLayer(layer, 30, -20, constrainAxis: false);

        Assert.Equal(1694, layer.X);
        Assert.Equal(906, layer.Y);
    }

    [Fact]
    public void Locked_layer_does_not_move_or_resize_from_preview()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Webcam");
        layer.IsLocked = true;
        var originalX = layer.X;
        var originalWidth = layer.Width;

        shell.Preview.MoveLayer(layer, 100, 100, constrainAxis: false);
        shell.Preview.ResizeLayer(layer, ResizeHandleKind.Right, 200, 0, keepAspect: false, fromCenter: false);

        Assert.Equal(originalX, layer.X);
        Assert.Equal(originalWidth, layer.Width);
    }

    [Fact]
    public void Preview_resize_layer_updates_dimensions()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Logo.png");

        shell.Preview.ResizeLayer(layer, ResizeHandleKind.BottomRight, 24, 16, keepAspect: false, fromCenter: false);

        Assert.Equal(200, layer.Width);
        Assert.Equal(120, layer.Height);
    }

    [Fact]
    public void Project_explorer_search_filters_visible_items()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        shell.ProjectExplorer.SearchText = "rtmp";

        Assert.Equal(1, shell.ProjectExplorer.VisibleItemCount);
        Assert.Equal("RTMP Twitch", shell.ProjectExplorer.Groups.Single(group => group.Title == "Outputs").VisibleItems.Single().Name);
    }

    [Fact]
    public void Add_source_mock_adds_layer_and_selects_it()
    {
        var shell = StudioDesignData.CreateShellViewModel();
        var originalCount = shell.BottomWorkbench.Layers.Count;

        shell.AddSourceCommand.Execute(null);

        Assert.Equal(originalCount + 1, shell.BottomWorkbench.Layers.Count);
        Assert.NotNull(shell.SelectedLayer);
        Assert.StartsWith("Text Overlay", shell.SelectedLayer!.Name, StringComparison.Ordinal);
        Assert.Same(shell.SelectedLayer, shell.Preview.SelectedLayer);
    }

    [Fact]
    public void Localization_manager_returns_shell_keys()
    {
        var loc = LocalizationManager.Instance;

        Assert.Equal("WTK MediaForge Studio", loc["App_Title"]);
        Assert.False(string.IsNullOrWhiteSpace(loc["Panel_ProjectExplorer"]));
        Assert.False(string.IsNullOrWhiteSpace(loc["Action_AddSource"]));
        Assert.False(string.IsNullOrWhiteSpace(loc["Status_Stopped"]));
    }

    [Fact]
    public void Streaming_and_recording_are_disabled_until_engine_runs()
    {
        var shell = StudioDesignData.CreateShellViewModel();

        Assert.False(shell.ToggleStreamingCommand.CanExecute(null));
        Assert.False(shell.ToggleRecordingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Stopping_engine_stops_outputs_first()
    {
        var clock = new FakeStudioClock();
        var services = StudioServiceFactory.CreateFake(clock: clock, uiTimer: new FakeStudioUiTimer());
        var shell = StudioDesignData.CreateShellViewModel(services);

        await shell.ToggleEngineCommand.ExecuteAsync(null);
        await shell.ToggleStreamingCommand.ExecuteAsync(null);
        await shell.ToggleRecordingCommand.ExecuteAsync(null);
        await shell.ToggleEngineCommand.ExecuteAsync(null);

        Assert.False(shell.IsEngineRunning);
        Assert.False(shell.IsStreaming);
        Assert.False(shell.IsRecording);
        Assert.Equal(StudioOutputUiState.Ready, shell.Toolbar.StreamingState);
        Assert.Equal(StudioOutputUiState.Ready, shell.Toolbar.RecordingState);
    }

    [Fact]
    public void Busy_engine_state_disables_engine_toggle()
    {
        var toolbar = new ToolbarViewModel
        {
            EngineState = StudioEngineUiState.Starting
        };

        Assert.True(toolbar.IsEngineBusy);
        Assert.Equal("busy", toolbar.EngineButtonClasses);
    }

    [Fact]
    public void Busy_output_state_disables_output_toggle()
    {
        var toolbar = new ToolbarViewModel
        {
            StreamingState = StudioOutputUiState.Starting,
            RecordingState = StudioOutputUiState.Stopping
        };

        Assert.True(toolbar.IsStreamBusy);
        Assert.True(toolbar.IsRecordingBusy);
        Assert.Equal("busy", toolbar.StreamButtonClasses);
        Assert.Equal("busy", toolbar.RecordingButtonClasses);
    }

    [Fact]
    public async Task Recording_elapsed_starts_at_zero()
    {
        var clock = new FakeStudioClock();
        var output = new FakeStudioOutputService(clock);

        await output.ToggleRecordingAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, output.RecordingElapsed);
    }

    [Fact]
    public async Task Recording_elapsed_advances_with_fake_clock()
    {
        var clock = new FakeStudioClock();
        var timer = new FakeStudioUiTimer();
        var services = StudioServiceFactory.CreateFake(clock: clock, uiTimer: timer);
        var shell = StudioDesignData.CreateShellViewModel(services);

        await shell.ToggleEngineCommand.ExecuteAsync(null);
        await shell.ToggleRecordingCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromSeconds(2));
        timer.RaiseTick();

        Assert.Equal("Recording 00:00:02", shell.Toolbar.RecordingButtonText);
    }

    [Fact]
    public async Task Recording_elapsed_resets_after_stop()
    {
        var clock = new FakeStudioClock();
        var output = new FakeStudioOutputService(clock);

        await output.ToggleRecordingAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(3));
        await output.ToggleRecordingAsync(CancellationToken.None);

        Assert.Null(output.RecordingStartedAt);
        Assert.Equal(TimeSpan.Zero, output.RecordingElapsed);
    }

    private static ProjectTreeItemViewModel FindItem(StudioShellViewModel shell, string itemName)
    {
        return shell.ProjectExplorer.Groups
            .SelectMany(group => group.Items)
            .Single(item => item.Name == itemName);
    }
}
