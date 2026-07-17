using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Models;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioSceneEditRuntimeServiceTests
{
    [Fact]
    public async Task Apply_commits_engine_session_and_closes_runtime_handle()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var scene = new StudioScene { Id = "scene-main", DisplayName = "Cena principal" };
        var layer = CreateLayer();

        var session = await service.BeginApplySessionAsync(scene);
        await service.TrackLayerVisualStateAsync(session, layer);
        await service.ApplySceneDraftAsync(session, transition: null);

        Assert.Equal(4, engine.Patches.Count);
        Assert.Equal(1, engine.CommitCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.TrackLayerVisualStateAsync(session, layer));
    }

    [Fact]
    public async Task Discard_discards_engine_session_and_closes_runtime_handle()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var scene = new StudioScene { Id = "scene-main", DisplayName = "Cena principal" };

        var session = await service.BeginApplySessionAsync(scene);
        await service.DiscardSceneDraftAsync(session);

        Assert.Equal(1, engine.DiscardCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ApplySceneDraftAsync(session, transition: null));
    }

    private static StudioLayer CreateLayer()
    {
        var layer = new StudioLayer
        {
            Id = "layer-camera",
            Name = "Camera",
            SourceName = "Camera",
            Type = "Source",
            IsVisible = true
        };
        layer.Transform.X = 10;
        layer.Transform.Y = 20;
        layer.Transform.Width = 320;
        layer.Transform.Height = 180;
        layer.Transform.Opacity = 75;
        layer.Effects.Add(new StudioEffect
        {
            Id = "layer-camera-effect-chroma",
            Name = "Chroma Key",
            IsEnabled = true
        });

        return layer;
    }

    private sealed class RecordingSceneEditEngine : IStudioSceneEditEngine
    {
        private CanvasId _canvasId;

        public List<SceneMutationPatch> Patches { get; } = [];

        public int CommitCallCount { get; private set; }

        public int DiscardCallCount { get; private set; }

        public ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
            CanvasId canvasId,
            SceneEditMode mode,
            CancellationToken cancellationToken = default)
        {
            _canvasId = canvasId;
            return ValueTask.FromResult(new SceneEditSessionDescriptor
            {
                SessionId = SceneEditSessionId.New(),
                CanvasId = canvasId,
                Mode = mode,
                BasePublishedVersionId = SceneVersionId.New(),
                DraftVersionId = SceneVersionId.New(),
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
            CommitCallCount++;
            return ValueTask.FromResult(new SceneCommitResult
            {
                SessionId = sessionId,
                CanvasId = _canvasId,
                OldVersionId = SceneVersionId.New(),
                NewVersionId = SceneVersionId.New(),
                AffectedCanvases = [_canvasId],
                AffectedOutputs = [],
                TransitionRequested = false
            });
        }

        public ValueTask DiscardSceneDraftAsync(
            SceneEditSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            DiscardCallCount++;
            return ValueTask.CompletedTask;
        }
    }
}
