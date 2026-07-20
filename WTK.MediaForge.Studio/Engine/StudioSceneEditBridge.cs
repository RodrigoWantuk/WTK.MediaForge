using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Engine;

public interface IStudioSceneEditEngine
{
    Task SynchronizeProjectAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken = default);

    ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
        CanvasId canvasId,
        SceneEditMode mode,
        CancellationToken cancellationToken = default);

    ValueTask ApplySceneMutationAsync(
        SceneEditSessionId sessionId,
        SceneMutationPatch patch,
        CancellationToken cancellationToken = default);

    ValueTask<SceneCommitResult> ApplySceneDraftAsync(
        SceneEditSessionId sessionId,
        SceneCommitRequest request,
        CancellationToken cancellationToken = default);

    ValueTask DiscardSceneDraftAsync(
        SceneEditSessionId sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class MediaForgeStudioSceneEditEngine(MediaForgeEngine engine) : IStudioSceneEditEngine
{
    private readonly MediaForgeEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public Task SynchronizeProjectAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (!_engine.HasProject || _engine.State is MediaForgeEngineState.Idle or MediaForgeEngineState.Loaded)
            return _engine.LoadProjectAsync(project, cancellationToken);

        return _engine.ApplyProjectUpdateAsync(
            editor => ReplaceProject(editor.Project, project),
            cancellationToken);
    }

    public ValueTask<SceneEditSessionDescriptor> BeginSceneEditSessionAsync(
        CanvasId canvasId,
        SceneEditMode mode,
        CancellationToken cancellationToken = default) =>
        _engine.BeginSceneEditSessionAsync(canvasId, mode, cancellationToken);

    public ValueTask ApplySceneMutationAsync(
        SceneEditSessionId sessionId,
        SceneMutationPatch patch,
        CancellationToken cancellationToken = default) =>
        _engine.ApplySceneMutationAsync(sessionId, patch, cancellationToken);

    public ValueTask<SceneCommitResult> ApplySceneDraftAsync(
        SceneEditSessionId sessionId,
        SceneCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _engine.ApplySceneDraftAsync(sessionId, request, cancellationToken);

    public ValueTask DiscardSceneDraftAsync(
        SceneEditSessionId sessionId,
        CancellationToken cancellationToken = default) =>
        _engine.DiscardSceneDraftAsync(sessionId, cancellationToken);

    private static void ReplaceProject(MediaForgeProject target, MediaForgeProject source)
    {
        var clone = MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(source));

        target.SchemaVersion = clone.SchemaVersion;
        target.CreatedWithVersion = clone.CreatedWithVersion;
        target.SavedWithVersion = clone.SavedWithVersion;

        target.SourceDefinitions.Clear();
        target.SourceDefinitions.AddRange(clone.SourceDefinitions);

        target.Canvases.Clear();
        target.Canvases.AddRange(clone.Canvases);

        target.Outputs.Clear();
        target.Outputs.AddRange(clone.Outputs);
    }
}

public sealed record StudioSceneEditBridgeSession(
    string StudioSceneId,
    SceneEditSessionDescriptor EngineSession)
{
    public CanvasId CanvasId => EngineSession.CanvasId;

    public SceneEditMode Mode => EngineSession.Mode;

    public SceneEditSessionId SessionId => EngineSession.SessionId;
}

public sealed class StudioSceneEditBridge(IStudioSceneEditEngine engine)
{
    private readonly IStudioSceneEditEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public ValueTask SynchronizeProjectAsync(
        MediaForgeProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ValueTask(_engine.SynchronizeProjectAsync(project, cancellationToken));
    }

    public async ValueTask<StudioSceneEditBridgeSession> BeginAsync(
        StudioScene scene,
        SceneEditMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (mode is not (SceneEditMode.Live or SceneEditMode.Apply))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported scene edit mode.");

        var canvasId = StudioEngineIdMap.CanvasId(scene.Id);
        var descriptor = await _engine
            .BeginSceneEditSessionAsync(canvasId, mode, cancellationToken)
            .ConfigureAwait(false);

        if (descriptor.CanvasId != canvasId)
            throw new InvalidOperationException("Engine returned a scene edit session for a different canvas.");

        return new StudioSceneEditBridgeSession(scene.Id, descriptor);
    }

    public async ValueTask ApplyLayerVisualStateAsync(
        StudioSceneEditBridgeSession session,
        StudioLayer layer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layer);

        await ApplyPatchAsync(session, StudioSceneMutationFactory.SetLayerTransform(layer), cancellationToken)
            .ConfigureAwait(false);
        await ApplyPatchAsync(session, StudioSceneMutationFactory.SetLayerVisibility(layer), cancellationToken)
            .ConfigureAwait(false);
        await ApplyPatchAsync(session, StudioSceneMutationFactory.SetLayerOpacity(layer), cancellationToken)
            .ConfigureAwait(false);
        await ApplyPatchAsync(session, StudioSceneMutationFactory.SetLayerEffects(layer), cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask ApplyPatchAsync(
        StudioSceneEditBridgeSession session,
        SceneMutationPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(patch);
        return _engine.ApplySceneMutationAsync(session.SessionId, patch, cancellationToken);
    }

    public ValueTask<SceneCommitResult> CommitAsync(
        StudioSceneEditBridgeSession session,
        StudioTransition? transition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Mode != SceneEditMode.Apply)
            throw new InvalidOperationException("Only Apply-mode Studio scene edit sessions can be committed.");

        var request = new SceneCommitRequest
        {
            TransitionPolicy = ToTransitionPolicy(transition)
        };
        return _engine.ApplySceneDraftAsync(session.SessionId, request, cancellationToken);
    }

    public ValueTask DiscardAsync(
        StudioSceneEditBridgeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Mode != SceneEditMode.Apply)
            throw new InvalidOperationException("Only Apply-mode Studio scene edit sessions can be discarded.");

        return _engine.DiscardSceneDraftAsync(session.SessionId, cancellationToken);
    }

    private static SceneApplyTransitionPolicy ToTransitionPolicy(StudioTransition? transition)
    {
        if (transition is null)
            return SceneApplyTransitionPolicy.UseOutputRoutePolicy;

        if (transition.Kind == StudioTransitionKind.Cut || transition.DurationMs <= 0)
        {
            return new SceneApplyTransitionPolicy
            {
                Kind = SceneApplyTransitionKind.Cut,
                Duration = TimeSpan.Zero
            };
        }

        return new SceneApplyTransitionPolicy
        {
            Kind = SceneApplyTransitionKind.Fade,
            Duration = TimeSpan.FromMilliseconds(transition.DurationMs)
        };
    }
}
