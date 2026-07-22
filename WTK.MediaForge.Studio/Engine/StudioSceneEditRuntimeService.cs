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
    private readonly ConcurrentDictionary<string, LiveMutationCoalescer> _liveMutations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _projectSyncGate = new(1, 1);
    private string? _syncedProjectFingerprint;

    public bool IsEngineBacked => true;

    public ValueTask TransitionOutputToSceneAsync(
        string outputId,
        string destinationSceneId,
        StudioTransition transition,
        CancellationToken cancellationToken = default) =>
        new(_bridge.TransitionOutputToSceneAsync(
            outputId,
            destinationSceneId,
            transition,
            cancellationToken).AsTask());

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

        return new StudioSceneEditRuntimeSession(runtimeSessionId, scene.Id, true, StudioSceneEditingMode.Draft);
    }

    public async ValueTask<StudioSceneEditRuntimeSession> BeginLiveSessionAsync(
        StudioDocument document,
        StudioScene scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        if (!document.Scenes.Any(candidate => string.Equals(candidate.Id, scene.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Scene '{scene.Id}' does not belong to the Studio document.");
        await SynchronizeProjectAsync(document, cancellationToken).ConfigureAwait(false);
        var engineSession = await _bridge.BeginAsync(scene, SceneEditMode.Live, cancellationToken).ConfigureAwait(false);
        var runtimeSessionId = Guid.NewGuid().ToString("N");
        _sessions[runtimeSessionId] = engineSession;
        _liveMutations[runtimeSessionId] = new LiveMutationCoalescer(
            patches => _bridge.ApplyBatchAsync(engineSession, patches, CancellationToken.None));
        return new StudioSceneEditRuntimeSession(runtimeSessionId, scene.Id, true, StudioSceneEditingMode.Live);
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

        if (session.Mode == StudioSceneEditingMode.Live)
        {
            if (!_liveMutations.TryGetValue(session.RuntimeSessionId, out var coalescer))
                throw new InvalidOperationException("Live scene mutation coalescer is missing or already closed.");
            await coalescer.EnqueueAsync(patches, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _bridge.ApplyBatchAsync(engineSession, patches, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<StudioSceneEditApplyResult> ApplySceneDraftAsync(
        StudioSceneEditRuntimeSession session,
        StudioTransition? transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Mode != StudioSceneEditingMode.Draft)
            throw new InvalidOperationException("Live scene edit sessions publish mutations and cannot be applied.");
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
        if (!_sessions.TryGetValue(session.RuntimeSessionId, out var engineSession))
            return;

        if (!string.Equals(engineSession.StudioSceneId, session.StudioSceneId, StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime scene edit session is bound to a different Studio scene.");

        Exception? failure = null;
        try
        {
            if (_liveMutations.TryRemove(session.RuntimeSessionId, out var coalescer))
            {
                try { await coalescer.FlushAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { failure = exception; }
            }
            try { await _bridge.DiscardAsync(engineSession, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
        }
        finally
        {
            _sessions.TryRemove(session.RuntimeSessionId, out _);
        }
        if (failure is not null)
            throw failure;
    }

    public async ValueTask DiscardAllSceneDraftsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in _sessions.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Exception? failure = null;
            try
            {
                if (_liveMutations.TryRemove(entry.Key, out var coalescer))
                {
                    try { await coalescer.FlushAsync(cancellationToken).ConfigureAwait(false); }
                    catch (Exception exception) { failure = exception; }
                }
                try { await _bridge.DiscardAsync(entry.Value, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }
            }
            finally
            {
                _sessions.TryRemove(entry.Key, out _);
            }
            if (failure is not null)
                throw failure;
        }
    }

    public ValueTask FlushLiveMutationsAsync(
        StudioSceneEditRuntimeSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _liveMutations.TryGetValue(session.RuntimeSessionId, out var coalescer)
            ? new ValueTask(coalescer.FlushAsync(cancellationToken))
            : ValueTask.CompletedTask;
    }

    public string? GetLastMutationError(StudioSceneEditRuntimeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _liveMutations.TryGetValue(session.RuntimeSessionId, out var coalescer)
            ? coalescer.LastError
            : null;
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

    public async ValueTask SynchronizeProjectAsync(
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

            await DiscardAllSceneDraftsAsync(cancellationToken)
                .ConfigureAwait(false);

            await _bridge
                .SynchronizeProjectAsync(project, cancellationToken)
                .ConfigureAwait(false);

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

    private sealed class LiveMutationCoalescer(
        Func<IReadOnlyList<SceneMutationPatch>, ValueTask> publish)
    {
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);
        private readonly object _gate = new();
        private readonly Func<IReadOnlyList<SceneMutationPatch>, ValueTask> _publish = publish;
        private IReadOnlyList<SceneMutationPatch>? _latest;
        private List<TaskCompletionSource> _waiters = [];
        private Task? _worker;
        private string? _lastError;

        public string? LastError => Volatile.Read(ref _lastError);

        public Task EnqueueAsync(IReadOnlyList<SceneMutationPatch> patches, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _latest = patches.ToArray();
                _waiters.Add(completion);
                _worker ??= RunAsync();
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task? worker;
                lock (_gate)
                    worker = _worker;
                if (worker is null)
                    return;
                await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                await Task.Delay(FrameInterval).ConfigureAwait(false);
                IReadOnlyList<SceneMutationPatch> patches;
                List<TaskCompletionSource> waiters;
                lock (_gate)
                {
                    patches = _latest!;
                    waiters = _waiters;
                    _latest = null;
                    _waiters = [];
                }
                try
                {
                    await _publish(patches).ConfigureAwait(false);
                    Volatile.Write(ref _lastError, null);
                    foreach (var waiter in waiters)
                        waiter.TrySetResult();
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref _lastError, exception.Message);
                    foreach (var waiter in waiters)
                        waiter.TrySetException(exception);
                }
                lock (_gate)
                {
                    if (_latest is null)
                    {
                        _worker = null;
                        return;
                    }
                }
            }
        }
    }
}
