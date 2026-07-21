using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Engine;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioSceneEditRuntimeServiceTests
{
    [Fact]
    public async Task Apply_commits_engine_session_and_closes_runtime_handle()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var scene = document.Scenes.Single(item => item.Id == "scene-main");
        var layer = scene.Layers.First();

        var session = await service.BeginApplySessionAsync(document, scene);
        await service.TrackLayerVisualStateAsync(session, layer);
        await service.ApplySceneDraftAsync(session, transition: null);

        Assert.Equal(1, engine.SyncCallCount);
        Assert.Equal(4, engine.Patches.Count);
        Assert.Equal(1, engine.CommitCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.TrackLayerVisualStateAsync(session, layer));
    }

    [Fact]
    public async Task Discard_discards_engine_session_and_closes_runtime_handle()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var scene = document.Scenes.Single(item => item.Id == "scene-main");

        var session = await service.BeginApplySessionAsync(document, scene);
        await service.DiscardSceneDraftAsync(session);

        Assert.Equal(1, engine.SyncCallCount);
        Assert.Equal(1, engine.DiscardCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.ApplySceneDraftAsync(session, transition: null));
    }

    [Fact]
    public async Task Begin_synchronizes_project_once_while_document_is_unchanged()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var scene = document.Scenes.Single(item => item.Id == "scene-main");

        _ = await service.BeginApplySessionAsync(document, scene);
        _ = await service.BeginApplySessionAsync(document, scene);

        Assert.Equal(1, engine.SyncCallCount);
        Assert.Equal(2, engine.BeginCallCount);
    }

    [Fact]
    public async Task Begin_resynchronizes_project_when_document_changes()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var scene = document.Scenes.Single(item => item.Id == "scene-main");

        _ = await service.BeginApplySessionAsync(document, scene);
        scene.DisplayName = "Cena principal editada";
        _ = await service.BeginApplySessionAsync(document, scene);

        Assert.Equal(2, engine.SyncCallCount);
    }

    [Fact]
    public async Task TrackSceneDraft_adds_new_layers_and_orders_final_draft()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var original = document.Scenes.Single(item => item.Id == "scene-main");
        var draft = SceneEditSessionService.CloneScene(original);
        var newLayer = new StudioLayer
        {
            Id = "layer-new-logo",
            Name = "New Logo",
            SourceId = "source-logo",
            SourceName = "Logo.png",
            Type = "Image",
            Order = original.Layers.Max(layer => layer.Order) + 1,
            IsVisible = true
        };
        newLayer.Transform.X = 80;
        newLayer.Transform.Y = 90;
        newLayer.Transform.Width = 220;
        newLayer.Transform.Height = 120;
        draft.Layers.Add(newLayer);

        var session = await service.BeginApplySessionAsync(document, original);
        await service.TrackSceneDraftAsync(session, document, original, draft);

        var add = Assert.IsType<SceneMutationPatch.AddLayer>(Assert.Single(engine.Patches));
        Assert.Equal(StudioEngineIdMap.DrawObjectId(newLayer.Id), add.Layer.Id);
        Assert.Equal(original.Layers.Count, add.Index);
        Assert.Equal(1, engine.BatchCallCount);
    }

    [Fact]
    public async Task TrackSceneDraft_with_no_changes_does_not_call_engine_batch()
    {
        var engine = new RecordingSceneEditEngine();
        var service = new StudioSceneEditRuntimeService(new StudioSceneEditBridge(engine));
        var document = StudioMockDocumentFactory.Create();
        var original = document.Scenes.Single(item => item.Id == "scene-main");
        var draft = SceneEditSessionService.CloneScene(original);
        var session = await service.BeginApplySessionAsync(document, original);

        await service.TrackSceneDraftAsync(session, document, original, draft);

        Assert.Equal(0, engine.BatchCallCount);
        Assert.Empty(engine.Patches);
    }

    private sealed class RecordingSceneEditEngine : IStudioSceneEditEngine
    {
        private CanvasId _canvasId;

        public List<SceneMutationPatch> Patches { get; } = [];

        public int SyncCallCount { get; private set; }

        public int BeginCallCount { get; private set; }

        public int CommitCallCount { get; private set; }

        public int DiscardCallCount { get; private set; }

        public int BatchCallCount { get; private set; }

        public Task SynchronizeProjectAsync(
            WTK.MediaForge.Composition.Project.MediaForgeProject project,
            CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            return Task.CompletedTask;
        }

        public ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
            CanvasId canvasId,
            SceneEditMode mode,
            CancellationToken cancellationToken = default)
        {
            BeginCallCount++;
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

        public ValueTask ApplySceneMutationsAsync(
            SceneEditSessionId sessionId,
            IReadOnlyList<SceneMutationPatch> patches,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            Patches.AddRange(patches);
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
