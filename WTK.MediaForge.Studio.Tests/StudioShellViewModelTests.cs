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
    public void Design_data_contains_product_groups_and_items()
    {
        var shell = CreateShell();

        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Cenas" && group.Items.Any(item => item.Name == "Main Scene"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Fontes" && group.Items.Any(item => item.Name == "Webcam"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Saidas" && group.Items.Any(item => item.Name == "RTMP Twitch"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Presets" && group.Items.Any(item => item.Name == "YouTube 1080p60"));
        Assert.Contains(shell.ProjectExplorer.Groups, group => group.Title == "Pacotes" && group.Items.Any(item => item.Name == "Brand Kit"));
        Assert.Equal(new[] { StudioBottomTabKind.Layers, StudioBottomTabKind.Effects, StudioBottomTabKind.Outputs }, shell.BottomWorkbench.Tabs.Select(tab => tab.Kind));
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
        shell.ConfirmDialogCommand.Execute(null);

        Assert.False(shell.Dialog.IsOpen);
        Assert.Equal(originalMainCount, main.Layers.Count);
        Assert.Equal(originalInterviewCount + 1, shell.CurrentScene!.Layers.Count);
        Assert.Equal(shell.SelectedLayer, shell.Preview.SelectedLayer);
    }

    [Fact]
    public void Source_inspector_can_add_existing_source_to_current_scene()
    {
        var shell = CreateShell();
        shell.SelectProjectItem(FindItem(shell, "Break BRB"));
        var originalCount = shell.CurrentScene!.Layers.Count;

        shell.SelectProjectItem(FindItem(shell, "Logo.png"));
        shell.AddSelectedSourceToCurrentSceneCommand.Execute(null);

        Assert.Equal(originalCount + 1, shell.CurrentScene!.Layers.Count);
        Assert.Equal("Logo.png", shell.SelectedLayer?.Name);
        Assert.IsType<LayerInspectorViewModel>(shell.Inspector.SelectedPage);
    }

    [Fact]
    public void Effects_are_contextual_to_selected_layer_and_clear_for_project_items()
    {
        var shell = CreateShell();
        var webcam = shell.BottomWorkbench.Layers.Single(layer => layer.Name == "Webcam");

        shell.SelectLayer(webcam);

        Assert.Contains(shell.BottomWorkbench.Effects, effect => effect.Name == "Chroma Key");
        Assert.Equal("Efeitos de Webcam", shell.BottomWorkbench.EffectsContextTitle);

        shell.SelectProjectItem(FindItem(shell, "Webcam"));

        Assert.Empty(shell.BottomWorkbench.Effects);
        Assert.Equal("Selecione uma camada", shell.BottomWorkbench.EffectsContextTitle);
    }

    [Fact]
    public void Output_route_change_updates_document_explorer_and_output_table()
    {
        var shell = CreateShell();
        shell.SelectProjectItem(FindItem(shell, "RTMP Twitch"));
        var inspector = Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        var interview = inspector.Scenes.Single(scene => scene.Name == "Interview");

        inspector.SelectedScene = interview;

        var output = shell.Document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        Assert.Equal("scene-interview", output.AssignedSceneId);
        Assert.Contains(shell.BottomWorkbench.Outputs, item => item.Name == "RTMP Twitch" && item.SceneName == "Interview");
        Assert.Contains("Interview", FindItem(shell, "RTMP Twitch").Metadata, StringComparison.Ordinal);
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
        Assert.Equal("Ao vivo", shell.Toolbar.StreamButtonText);
        Assert.StartsWith("Gravando", shell.Toolbar.RecordingButtonText, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_stream_key_is_masked_in_properties()
    {
        var shell = CreateShell();

        shell.SelectProjectItem(FindItem(shell, "RTMP Twitch"));

        var inspector = Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal("sk_live_************", inspector.MaskedStreamKey);
        Assert.DoesNotContain("2d97c8a6", inspector.MaskedStreamKey, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_secret", inspector.MaskedStreamKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_view_model_supports_zoom_pan_move_resize_nudge_and_lock()
    {
        var shell = CreateShell();
        var layer = shell.BottomWorkbench.Layers.Single(item => item.Name == "Logo.png");

        shell.Preview.SetViewport(1280, 720);
        Assert.Equal("Ajustar", shell.Preview.ZoomLabel);

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
    }

    [Fact]
    public void Visible_resources_do_not_mix_languages()
    {
        var loc = LocalizationManager.Instance;
        var values = new[]
        {
            loc["Panel_ProjectExplorer"],
            loc["Panel_Properties"],
            loc["Panel_Layers"],
            loc["Panel_Effects"],
            loc["Panel_Outputs"],
            loc["Action_AddSource"],
            loc["Action_AddScene"],
            loc["Action_ConfigureOutput"],
            loc["Action_StartStreaming"],
            loc["Action_StartRecording"]
        };

        Assert.Equal("pt-BR", loc.CurrentCulture.Name);
        Assert.Contains("Propriedades", values);
        Assert.DoesNotContain(values, value => value.Contains("Inspector", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("Engine", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("Output Monitor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("Timeline", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(values, value => value.Contains("Audio Mixer", StringComparison.OrdinalIgnoreCase));
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
