using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Models;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioSceneEditBridgeTests
{
    [Fact]
    public async Task Begin_uses_deterministic_canvas_id_and_requested_mode()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var scene = new StudioScene { Id = "scene-main", DisplayName = "Main" };

        var session = await bridge.BeginAsync(scene, SceneEditMode.Apply);

        Assert.Equal("scene-main", session.StudioSceneId);
        Assert.Equal(SceneEditMode.Apply, session.Mode);
        Assert.Equal(StudioEngineIdMap.CanvasId("scene-main"), engine.BeginCanvasId);
        Assert.Equal(SceneEditMode.Apply, engine.BeginMode);
    }

    [Fact]
    public async Task Apply_layer_visual_state_sends_engine_mutation_patches_in_order()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var scene = new StudioScene { Id = "scene-main", DisplayName = "Main" };
        var session = await bridge.BeginAsync(scene, SceneEditMode.Apply);
        var layer = new StudioLayer
        {
            Id = "layer-camera",
            Name = "Camera",
            SourceName = "Camera",
            Type = "Source",
            IsVisible = false
        };
        layer.Transform.X = 10;
        layer.Transform.Y = 20;
        layer.Transform.Width = 300;
        layer.Transform.Height = 200;
        layer.Transform.Opacity = 63;
        layer.Effects.Add(new StudioEffect
        {
            Id = "layer-camera-effect-chroma",
            Name = "Chroma Key",
            IsEnabled = true
        });

        await bridge.ApplyLayerVisualStateAsync(session, layer);

        Assert.Equal(4, engine.Patches.Count);
        Assert.IsType<SceneMutationPatch.SetLayerTransform>(engine.Patches[0]);
        Assert.IsType<SceneMutationPatch.SetLayerVisibility>(engine.Patches[1]);
        Assert.IsType<SceneMutationPatch.SetLayerOpacity>(engine.Patches[2]);
        Assert.IsType<SceneMutationPatch.SetLayerEffects>(engine.Patches[3]);
        Assert.All(engine.Patches, patch =>
        {
            var layerId = patch switch
            {
                SceneMutationPatch.SetLayerTransform item => item.LayerId,
                SceneMutationPatch.SetLayerVisibility item => item.LayerId,
                SceneMutationPatch.SetLayerOpacity item => item.LayerId,
                SceneMutationPatch.SetLayerEffects item => item.LayerId,
                _ => default
            };
            Assert.Equal(StudioEngineIdMap.DrawObjectId("layer-camera"), layerId);
        });
    }

    [Fact]
    public async Task Commit_maps_studio_transition_to_scene_apply_policy()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var session = await bridge.BeginAsync(new StudioScene { Id = "scene-main" }, SceneEditMode.Apply);

        await bridge.CommitAsync(
            session,
            new StudioTransition
            {
                Id = "transition-fade",
                DisplayName = "Fade",
                Kind = StudioTransitionKind.Fade,
                DurationMs = 350
            });

        Assert.NotNull(engine.CommitRequest);
        Assert.Equal(SceneApplyTransitionKind.Fade, engine.CommitRequest!.TransitionPolicy.Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(350), engine.CommitRequest.TransitionPolicy.Duration);
    }

    [Fact]
    public async Task Commit_maps_studio_cut_to_zero_duration_cut_policy()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var session = await bridge.BeginAsync(new StudioScene { Id = "scene-main" }, SceneEditMode.Apply);

        await bridge.CommitAsync(
            session,
            new StudioTransition
            {
                Id = "transition-cut",
                DisplayName = "Cut",
                Kind = StudioTransitionKind.Cut,
                DurationMs = 0
            });

        Assert.NotNull(engine.CommitRequest);
        Assert.Equal(SceneApplyTransitionKind.Cut, engine.CommitRequest!.TransitionPolicy.Kind);
        Assert.Equal(TimeSpan.Zero, engine.CommitRequest.TransitionPolicy.Duration);
    }

    [Fact]
    public async Task Commit_without_explicit_transition_uses_output_route_policy()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var session = await bridge.BeginAsync(new StudioScene { Id = "scene-main" }, SceneEditMode.Apply);

        await bridge.CommitAsync(session);

        Assert.NotNull(engine.CommitRequest);
        Assert.Equal(SceneApplyTransitionKind.UseOutputRoutePolicy, engine.CommitRequest!.TransitionPolicy.Kind);
    }

    [Fact]
    public async Task Discard_rejects_live_sessions()
    {
        var engine = new RecordingSceneEditEngine();
        var bridge = new StudioSceneEditBridge(engine);
        var session = await bridge.BeginAsync(new StudioScene { Id = "scene-main" }, SceneEditMode.Live);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bridge.DiscardAsync(session));
    }

    private sealed class RecordingSceneEditEngine : IStudioSceneEditEngine
    {
        public CanvasId BeginCanvasId { get; private set; }

        public SceneEditMode BeginMode { get; private set; }

        public List<SceneMutationPatch> Patches { get; } = [];

        public SceneCommitRequest? CommitRequest { get; private set; }

        public Task SynchronizeProjectAsync(
            MediaForgeProject project,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
            CanvasId canvasId,
            SceneEditMode mode,
            CancellationToken cancellationToken = default)
        {
            BeginCanvasId = canvasId;
            BeginMode = mode;
            return ValueTask.FromResult(new SceneEditSessionDescriptor
            {
                SessionId = SceneEditSessionId.New(),
                CanvasId = canvasId,
                Mode = mode,
                BasePublishedVersionId = SceneVersionId.New(),
                DraftVersionId = mode == SceneEditMode.Apply ? SceneVersionId.New() : null,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        public ValueTask ApplySceneMutationAsync(
            SceneEditSessionId sessionId,
            SceneMutationPatch patch,
            CancellationToken cancellationToken = default)
        {
            Patches.Add(patch);
            return ValueTask.CompletedTask;
        }

        public ValueTask<SceneCommitResult> ApplySceneDraftAsync(
            SceneEditSessionId sessionId,
            SceneCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitRequest = request;
            return ValueTask.FromResult(new SceneCommitResult
            {
                SessionId = sessionId,
                CanvasId = BeginCanvasId,
                OldVersionId = SceneVersionId.New(),
                NewVersionId = SceneVersionId.New(),
                AffectedCanvases = [BeginCanvasId],
                AffectedOutputs = [],
                TransitionRequested = request.TransitionPolicy.Kind == SceneApplyTransitionKind.Fade
            });
        }

        public ValueTask DiscardSceneDraftAsync(
            SceneEditSessionId sessionId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
