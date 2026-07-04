using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Models;
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

        Assert.Equal("VIS", layer.VisibilityGlyph);
        Assert.Equal("EDIT", layer.LockGlyph);

        shell.ToggleLayerVisibilityCommand.Execute(layer);
        shell.ToggleLayerLockCommand.Execute(layer);

        Assert.Equal("HID", layer.VisibilityGlyph);
        Assert.Equal("LOCK", layer.LockGlyph);
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

    private static ProjectTreeItemViewModel FindItem(StudioShellViewModel shell, string itemName)
    {
        return shell.ProjectExplorer.Groups
            .SelectMany(group => group.Items)
            .Single(item => item.Name == itemName);
    }
}
