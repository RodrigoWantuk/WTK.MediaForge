using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.Engine;

public sealed class StudioSceneEditRuntimeService(
    StudioSceneEditBridge bridge,
    StudioProjectEngineMapper? projectMapper = null) : IStudioSceneEditRuntimeService
{
    private readonly StudioSceneEditBridge _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    private readonly StudioProjectEngineMapper _projectMapper = projectMapper ?? new StudioProjectEngineMapper();
    private readonly ConcurrentDictionary<string, StudioSceneEditBridgeSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _projectSyncGate = new(1, 1);
    private string? _syncedProjectFingerprint;

    public bool IsEngineBacked => true;

    public async ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(
        StudioDocument document,
        StudioScene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        if (!document.Scenes.Any(candidate => string.Equals(candidate.Id, scene.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Scene '{scene.Id}' does not belong to the Studio document.");

        await SynchronizeProjectAsync(document, cancellationToken)
            .ConfigureAwait(false);

        var engineSession = await _bridge
            .BeginAsync(scene, SceneEditMode.Apply, cancellationToken)
            .ConfigureAwait(false);
        var runtimeSessionId = Guid.NewGuid().ToString("N");
        _sessions[runtimeSessionId] = engineSession;

        return new StudioSceneEditRuntimeSession(runtimeSessionId, scene.Id, true);
    }

    public ValueTask TrackLayerVisualStateAsync(
        StudioSceneEditRuntimeSession session,
        StudioLayer layer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layer);
        var engineSession = Resolve(session);

        return _bridge.ApplyLayerVisualStateAsync(engineSession, layer, cancellationToken);
    }

    public async ValueTask TrackSceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioDocument document,
        StudioScene originalScene,
        StudioScene draftScene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(originalScene);
        ArgumentNullException.ThrowIfNull(draftScene);
        if (!string.Equals(session.StudioSceneId, originalScene.Id, StringComparison.Ordinal) ||
            !string.Equals(session.StudioSceneId, draftScene.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime scene edit session and draft scenes are bound to different Studio scenes.");
        }

        var engineSession = Resolve(session);
        var patches = new SceneMutationBatchBuilder(_projectMapper).Build(document, originalScene, draftScene);
        if (patches.Count == 0)
            return;

        await _bridge.ApplyBatchAsync(engineSession, patches, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioTransition? transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var engineSession = Resolve(session);

        var result = await _bridge
            .CommitAsync(engineSession, transition, cancellationToken)
            .ConfigureAwait(false);
        _sessions.TryRemove(session.RuntimeSessionId, out _);

        return new StudioSceneEditApplyResult(
            true,
            result.AffectedOutputs.Select(outputId => outputId.Value.ToString()).ToArray());
    }

    public async ValueTask DiscardSceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var engineSession = Resolve(session);

        try
        {
            await _bridge.DiscardAsync(engineSession, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessions.TryRemove(session.RuntimeSessionId, out _);
        }
    }

    private StudioSceneEditBridgeSession Resolve(StudioSceneEditRuntimeSession session)
    {
        if (!session.IsEngineBacked)
            throw new InvalidOperationException("Runtime session is not engine-backed.");

        if (!_sessions.TryGetValue(session.RuntimeSessionId, out var engineSession))
            throw new InvalidOperationException("Engine-backed scene edit session is missing or already closed.");

        if (!string.Equals(engineSession.StudioSceneId, session.StudioSceneId, StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime scene edit session is bound to a different Studio scene.");

        return engineSession;
    }

    private async ValueTask SynchronizeProjectAsync(
        StudioDocument document,
        CancellationToken cancellationToken)
    {
        var project = _projectMapper.CreateProject(document);
        var fingerprint = ComputeProjectFingerprint(project);
        if (string.Equals(_syncedProjectFingerprint, fingerprint, StringComparison.Ordinal))
            return;

        await _projectSyncGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (string.Equals(_syncedProjectFingerprint, fingerprint, StringComparison.Ordinal))
                return;

            await _bridge
                .SynchronizeProjectAsync(project, cancellationToken)
                .ConfigureAwait(false);

            _sessions.Clear();
            _syncedProjectFingerprint = fingerprint;
        }
        finally
        {
            _projectSyncGate.Release();
        }
    }

    private static string ComputeProjectFingerprint(MediaForgeProject project)
    {
        var json = MediaForgeProjectSerializer.Serialize(project);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
