using System.Collections.Concurrent;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;

namespace WTK.MediaForge.Studio.Engine;

public sealed class StudioSceneEditRuntimeService(StudioSceneEditBridge bridge) : IStudioSceneEditRuntimeService
{
    private readonly StudioSceneEditBridge _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    private readonly ConcurrentDictionary<string, StudioSceneEditBridgeSession> _sessions = new(StringComparer.Ordinal);

    public bool IsEngineBacked => true;

    public async ValueTask<StudioSceneEditRuntimeSession> BeginApplySessionAsync(
        StudioScene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
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
}
