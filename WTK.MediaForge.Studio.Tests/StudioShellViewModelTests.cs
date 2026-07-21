using Avalonia;
using Avalonia.Input;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
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
    public void Add_source_dialog_is_capability_driven()
    {
        var shell = CreateShell();

        shell.AddSourceCommand.Execute(null);

        var image = shell.Dialog.Options.Single(option => option.Id == "source.image");
        var webcam = shell.Dialog.Options.Single(option => option.Id == "source.webcam");
        var media = shell.Dialog.Options.Single(option => option.Id == "source.media");
        var ndi = shell.Dialog.Options.Single(option => option.Id == "source.ndi");

        Assert.True(image.IsEnabled);
        Assert.Equal("Suportado", image.Badge);
        Assert.False(webcam.IsEnabled);
        Assert.Equal("Indisponível", webcam.Badge);
        Assert.Contains("capability", webcam.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(media.IsEnabled);
        Assert.Contains("decode hardware", media.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(ndi.IsEnabled);
        Assert.Equal("Bloqueado", ndi.Badge);
        Assert.DoesNotContain(shell.Dialog.Options, option => option.Badge == "Disponível");
    }

    [Fact]
    public void Configure_output_dialog_is_capability_driven()
    {
        var shell = CreateShell();

        shell.ConfigureOutputCommand.Execute(null);

        var preview = shell.Dialog.Options.Single(option => option.Id == "output.preview");
        var mp4 = shell.Dialog.Options.Single(option => option.Id == "output.file.mp4");
        var rtmp = shell.Dialog.Options.Single(option => option.Id == "output.rtmp");
        var virtualCamera = shell.Dialog.Options.Single(option => option.Id == "output.virtual-camera");

        Assert.True(preview.IsEnabled);
        Assert.Equal("Suportado", preview.Badge);
        Assert.False(mp4.IsEnabled);
        Assert.Equal("Indisponível", mp4.Badge);
        Assert.Contains("hardware encode", mp4.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(rtmp.IsEnabled);
        Assert.Contains("prova RTMP", rtmp.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(virtualCamera.IsEnabled);
        Assert.Equal("Planejado", virtualCamera.Badge);

        preview.SelectCommand!.Execute(null);

        Assert.False(shell.Dialog.IsOpen);
        Assert.IsType<OutputInspectorViewModel>(shell.Inspector.SelectedPage);
        Assert.Equal(StudioSelectionKind.Output, shell.CurrentSelection.Kind);
        Assert.Equal("output-preview", shell.CurrentSelection.EntityId);
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
    public void RouteOutputDialog_UsesAlterarOrTransicionar_NotEnviar()
    {
        var shell = CreateShell();
        var output = shell.Document.Outputs.Single(item => item.Id == "output-rtmp-twitch");

        output.IsLive = false;
        output.State = StudioOutputState.Running;
        shell.Production.Outputs.Single(item => item.Id == output.Id).SendSceneCommand!.Execute(null);

        Assert.Equal("Alterar", shell.Dialog.PrimaryText);
        Assert.Equal("Cancelar", shell.Dialog.SecondaryText);
        Assert.Contains("Alterar cena", shell.Dialog.Title);
        Assert.DoesNotContain("Enviar cena", shell.Dialog.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enviar cena", shell.Dialog.PrimaryText, StringComparison.OrdinalIgnoreCase);

        shell.CancelDialogCommand.Execute(null);
        output.IsLive = true;
        output.State = StudioOutputState.Live;
        shell.Production.Outputs.Single(item => item.Id == output.Id).SendSceneCommand!.Execute(null);

        Assert.Equal("Transicionar", shell.Dialog.PrimaryText);
        Assert.NotEqual("Cancelar", shell.Dialog.PrimaryText);
        Assert.Contains("Transicionar cena", shell.Dialog.Title);
    }

    [Fact]
    public void RouteOutputDialog_SelectionDoesNotApplyUntilConfirmed()
    {
        var shell = CreateShell();
        var output = shell.Document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        var originalScene = output.AssignedSceneId;

        shell.Production.Outputs.Single(item => item.Id == output.Id).SendSceneCommand!.Execute(null);
        shell.Dialog.Options.Single(item => item.Id == "scene-brb").SelectCommand!.Execute(null);

        Assert.Equal(originalScene, output.AssignedSceneId);
        Assert.Equal("scene-brb", shell.Dialog.SelectedSceneId);

        shell.ConfirmDialogCommand.Execute(null);

        Assert.Equal("scene-brb", output.AssignedSceneId);
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
    public void SceneEditSession_DraftChangesDoNotUpdateAppliedOutput()
    {
        var shell = CreateShell();
        var savedScene = shell.Document.Scenes.Single(item => item.Id == "scene-main");
        var savedLayer = savedScene.Layers.First(item => !item.IsLocked);
        var output = shell.Document.Outputs.First(item => item.AssignedSceneId == "scene-main");
        var snapshotX = output.AppliedSceneSnapshot!.Layers.Single(item => item.Id == savedLayer.Id).Transform.X;
        var draftLayer = shell.BottomWorkbench.Layers.Single(item => item.Id == savedLayer.Id);

        shell.Preview.MoveLayerFromStart(draftLayer, draftLayer.X, draftLayer.Y, new Vector(80, 0), KeyModifiers.None);

        Assert.True(shell.Preview.HasPendingChanges);
        Assert.Equal(snapshotX, output.AppliedSceneSnapshot!.Layers.Single(item => item.Id == savedLayer.Id).Transform.X);
        Assert.Equal(snapshotX, savedLayer.Transform.X);
        Assert.NotEqual(snapshotX, draftLayer.X);
    }

    [Fact]
    public async Task SceneEditSession_CanApplyOrDiscardChanges()
    {
        var shell = CreateShell();
        var savedScene = shell.Document.Scenes.Single(item => item.Id == "scene-main");
        var savedLayer = savedScene.Layers.First(item => !item.IsLocked);
        var draftLayer = shell.BottomWorkbench.Layers.Single(item => item.Id == savedLayer.Id);

        shell.Preview.MoveLayerFromStart(draftLayer, draftLayer.X, draftLayer.Y, new Vector(80, 0), KeyModifiers.None);
        var editedX = draftLayer.X;
        await shell.ApplySceneDraftCommand.ExecuteAsync(null);

        Assert.False(shell.Preview.HasPendingChanges);
        var updatedSavedLayer = savedScene.Layers.Single(item => item.Id == savedLayer.Id);
        Assert.Equal(draftLayer.Id, updatedSavedLayer.Id);
        Assert.Equal(editedX, updatedSavedLayer.Transform.X);
        Assert.DoesNotContain(shell.Document.Outputs, item => item.HasPendingSceneUpdate);

        var appliedX = updatedSavedLayer.Transform.X;
        var secondDraft = shell.BottomWorkbench.Layers.Single(item => item.Id == updatedSavedLayer.Id);
        shell.Preview.MoveLayerFromStart(secondDraft, secondDraft.X, secondDraft.Y, new Vector(100, 0), KeyModifiers.None);
        await shell.DiscardSceneDraftCommand.ExecuteAsync(null);

        Assert.False(shell.Preview.HasPendingChanges);
        Assert.Equal(appliedX, savedScene.Layers.Single(item => item.Id == updatedSavedLayer.Id).Transform.X);
        Assert.Equal(appliedX, shell.BottomWorkbench.Layers.Single(item => item.Id == updatedSavedLayer.Id).X);
    }

    [Fact]
    public async Task SceneEditSession_PushesDraftLayerStateToRuntimeBeforeApply()
    {
        var runtime = new RecordingSceneEditRuntimeService();
        var shell = CreateShell(runtime);
        var draftLayer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);

        shell.Preview.MoveLayerFromStart(draftLayer, draftLayer.X, draftLayer.Y, new Vector(80, 0), KeyModifiers.None);
        var editedX = draftLayer.X;

        await shell.ApplySceneDraftCommand.ExecuteAsync(null);

        Assert.Equal(["scene-main"], runtime.BegunSceneIds);
        Assert.Equal(1, runtime.ApplyCallCount);
        Assert.Equal(1, runtime.TrackedSceneDraftCount);
        Assert.Contains(runtime.TrackedLayers, item => item.Id == draftLayer.Id && item.X == editedX);
        Assert.False(shell.Preview.HasPendingChanges);
    }

    [Fact]
    public async Task Apply_updates_only_outputs_returned_by_engine()
    {
        var runtime = new RecordingSceneEditRuntimeService();
        var shell = CreateShell(runtime);
        var affected = shell.Document.Outputs.First(output => output.AssignedSceneId == "scene-main");
        var unrelated = shell.Document.Outputs.Last(output => output.Id != affected.Id);
        affected.HasPendingSceneUpdate = true;
        unrelated.HasPendingSceneUpdate = true;
        runtime.AffectedOutputIds = [affected.Id];
        var draftLayer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);
        shell.Preview.MoveLayerFromStart(draftLayer, draftLayer.X, draftLayer.Y, new Vector(10, 0), KeyModifiers.None);

        await shell.ApplySceneDraftCommand.ExecuteAsync(null);

        Assert.False(affected.HasPendingSceneUpdate);
        Assert.True(unrelated.HasPendingSceneUpdate);
    }

    [Fact]
    public async Task SceneEditSession_PushesNewDraftLayersToRuntimeBeforeApply()
    {
        var runtime = new RecordingSceneEditRuntimeService();
        var shell = CreateShell(runtime);

        shell.AddSourceCommand.Execute(null);
        shell.Dialog.Options.Single(option => option.Id == "source.image").SelectCommand!.Execute(null);
        var newLayer = shell.SelectedLayer!;

        await shell.ApplySceneDraftCommand.ExecuteAsync(null);

        Assert.Contains(runtime.TrackedLayers, item => item.Id == newLayer.Id);
        Assert.Contains(runtime.TrackedSceneLayerIds.Single(), id => id == newLayer.Id);
    }

    [Fact]
    public void Layer_order_visibility_and_effect_commands_mark_scene_draft_changed()
    {
        var shell = CreateShell();
        var layer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);

        shell.ToggleLayerVisibilityCommand.Execute(layer);

        Assert.True(shell.Preview.HasPendingChanges);
        Assert.True(shell.ApplySceneDraftCommand.CanExecute(null));
    }

    [Fact]
    public async Task SceneEditSession_DiscardDiscardsRuntimeDraftWithoutApplying()
    {
        var runtime = new RecordingSceneEditRuntimeService();
        var shell = CreateShell(runtime);
        var draftLayer = shell.BottomWorkbench.Layers.First(item => !item.IsLocked);

        shell.Preview.MoveLayerFromStart(draftLayer, draftLayer.X, draftLayer.Y, new Vector(80, 0), KeyModifiers.None);

        await shell.DiscardSceneDraftCommand.ExecuteAsync(null);

        Assert.Equal(1, runtime.DiscardCallCount);
        Assert.Equal(0, runtime.ApplyCallCount);
        Assert.False(shell.Preview.HasPendingChanges);
    }

    [Fact]
    public void SafeArea_DefaultComesFromLinkedOutputProfile()
    {
        var shell = CreateShell();

        Assert.Equal(5, shell.Preview.SafeAreaMarginPercent);
        Assert.Equal(SafeAreaDisplayMode.Visible, shell.Preview.SafeAreaMode);
        Assert.Contains("Prévia local", shell.Preview.SafeAreaProfileLabel, StringComparison.Ordinal);
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

    private static StudioShellViewModel CreateShell(IStudioSceneEditRuntimeService? sceneEditRuntimeService = null)
    {
        var services = StudioServiceFactory.CreateFake(
            sceneEditRuntimeService: sceneEditRuntimeService,
            uiTimer: new FakeStudioUiTimer());
        return StudioDesignData.CreateShellViewModel(services);
    }

    private sealed class RecordingSceneEditRuntimeService : IStudioSceneEditRuntimeService
    {
        private readonly List<TrackedLayerState> _trackedLayers = [];

        public bool IsEngineBacked => true;

        public List<string> BegunSceneIds { get; } = [];

        public IReadOnlyList<TrackedLayerState> TrackedLayers => _trackedLayers;

        public int ApplyCallCount { get; private set; }

        public int DiscardCallCount { get; private set; }

        public int TrackedSceneDraftCount { get; private set; }

        public IReadOnlyList<string> AffectedOutputIds { get; set; } = [];

        public List<string[]> TrackedSceneLayerIds { get; } = [];

        public ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(
            StudioDocument document,
            StudioScene scene,
            CancellationToken cancellationToken = default)
        {
            BegunSceneIds.Add(scene.Id);
            return ValueTask.FromResult(new StudioSceneEditRuntimeSession(Guid.NewGuid().ToString("N"), scene.Id, true));
        }

        public ValueTask TrackLayerVisualStateAsync(
            StudioSceneEditRuntimeSession session,
            StudioLayer layer,
            CancellationToken cancellationToken = default)
        {
            _trackedLayers.Add(new TrackedLayerState(layer.Id, layer.Transform.X, layer.Transform.Y, layer.Transform.Width, layer.Transform.Height));
            return ValueTask.CompletedTask;
        }

        public ValueTask TrackSceneDraftAsync(
            StudioSceneEditRuntimeSession session,
            StudioDocument document,
            StudioScene originalScene,
            StudioScene draftScene,
            CancellationToken cancellationToken = default)
        {
            TrackedSceneDraftCount++;
            TrackedSceneLayerIds.Add(draftScene.Layers.Select(layer => layer.Id).ToArray());
            foreach (var layer in draftScene.Layers)
            {
                _trackedLayers.Add(new TrackedLayerState(layer.Id, layer.Transform.X, layer.Transform.Y, layer.Transform.Width, layer.Transform.Height));
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(
            StudioSceneEditRuntimeSession session,
            StudioTransition? transition,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            return ValueTask.FromResult(new StudioSceneEditApplyResult(true, AffectedOutputIds));
        }

        public ValueTask DiscardSceneDraftAsync(
            StudioSceneEditRuntimeSession session,
            CancellationToken cancellationToken = default)
        {
            DiscardCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record TrackedLayerState(string Id, double X, double Y, double Width, double Height);
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

    [Fact]
    public void PreviewInlineVisibilityToggle_HitTestUsesViewportRect()
    {
        var rect = new Rect(100, 80, 320, 180);
        var toggle = SceneEditorHitTest.VisibilityToggleRect(rect);

        Assert.True(SceneEditorHitTest.HitTestVisibilityToggle(rect, toggle.Center));
        Assert.False(SceneEditorHitTest.HitTestVisibilityToggle(rect, new Point(rect.Left + 4, rect.Bottom - 4)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void Hit_test_selects_rotated_layer_visual_area(double rotationDegrees)
    {
        var layer = CreateLayer("Rotated", 100, 100, 200, 100, order: 1);
        layer.RotationDegrees = rotationDegrees;
        var center = new Point(layer.X + layer.Width / 2d, layer.Y + layer.Height / 2d);

        var hit = SceneEditorHitTest.HitTestLayer([layer], center);

        Assert.Equal(layer, hit);
    }

    [Fact]
    public void Rotated_layer_hit_test_rejects_axis_aligned_corner_outside_visual_shape()
    {
        var layer = CreateLayer("Rotated", 100, 100, 200, 100, order: 1);
        layer.RotationDegrees = 45;
        var axisAlignedCorner = new Point(layer.X + 2, layer.Y + 2);

        Assert.False(SceneEditorHitTest.LayerContainsScenePoint(layer, axisAlignedCorner));
        Assert.Null(SceneEditorHitTest.HitTestLayer([layer], axisAlignedCorner));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void Resize_handles_follow_rotated_layer_corners(double rotationDegrees)
    {
        var layer = CreateLayer("Rotated", 100, 100, 200, 100, order: 1);
        layer.RotationDegrees = rotationDegrees;
        var transform = new SceneEditorTransform();

        var corners = SceneEditorHitTest.LayerViewportCorners(layer, transform);
        var handles = SceneEditorHitTest.HandleRects(corners, 10);

        Assert.Equal(ResizeHandleKind.TopLeft, SceneEditorHitTest.HitTestResizeHandle(corners, corners[0], 10));
        Assert.Equal(ResizeHandleKind.TopRight, SceneEditorHitTest.HitTestResizeHandle(corners, corners[1], 10));
        Assert.Equal(ResizeHandleKind.BottomRight, SceneEditorHitTest.HitTestResizeHandle(corners, corners[2], 10));
        Assert.Equal(ResizeHandleKind.BottomLeft, SceneEditorHitTest.HitTestResizeHandle(corners, corners[3], 10));
        Assert.All(handles, handle => Assert.True(handle.Rect.Width > 0 && handle.Rect.Height > 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    public void Visibility_toggle_follows_rotated_layer_top_right_corner(double rotationDegrees)
    {
        var layer = CreateLayer("Rotated", 100, 100, 200, 100, order: 1);
        layer.RotationDegrees = rotationDegrees;
        var transform = new SceneEditorTransform();

        var corners = SceneEditorHitTest.LayerViewportCorners(layer, transform);
        var toggle = SceneEditorHitTest.VisibilityToggleRect(corners);

        Assert.True(SceneEditorHitTest.HitTestVisibilityToggle(corners, toggle.Center));
        Assert.Equal(corners[1].X - SceneEditorHitTest.VisibilityToggleSize - 8, toggle.X, precision: 3);
        Assert.Equal(corners[1].Y + 8, toggle.Y, precision: 3);
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
