using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Recovery;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Engine;

internal sealed class EngineProjectCoordinator
{
    public MediaForgeProject? Current { get; set; }

    public MediaForgeProject? CreatePublicSnapshot() =>
        Current is null ? null : MediaForgeProjectCloner.DeepClone(Current);
}

internal sealed class SceneEditSessionCoordinator<TSession> where TSession : class
{
    public Dictionary<SceneEditSessionId, TSession> Sessions { get; } = [];

    public TSession Require(SceneEditSessionId id) => Sessions.TryGetValue(id, out var session)
        ? session
        : throw new InvalidOperationException($"Scene edit session {id} is not active.");

    public void Replace(SceneEditSessionId id, TSession session) => Sessions[id] = session;

    public bool TryGet(SceneEditSessionId id, out TSession session) => Sessions.TryGetValue(id, out session!);
}

internal sealed class EngineLifecycleCoordinator : IDisposable
{
    private int _state = (int)MediaForgeEngineState.Idle;
    private int _disposed;

    public SemaphoreSlim Gate { get; } = new(1, 1);
    public MediaForgeEngineState State => (MediaForgeEngineState)Volatile.Read(ref _state);
    public bool TryBeginDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void SetState(MediaForgeEngineState state) => Volatile.Write(ref _state, (int)state);
    public void Dispose() => Gate.Dispose();
}

internal sealed class EngineOutputRouteCoordinator<TEntry>
{
    public Dictionary<RenderOutputId, TEntry> Sinks { get; } = [];
}

internal static class EngineHealthCoordinator
{
    public static MediaForgeRuntimeHealthStatus ResolveStatus(
        MediaForgeEngineState engineState,
        IReadOnlyCollection<FaultRecoveryState> recoveries,
        IReadOnlyCollection<EncodedOutputRuntimeSnapshot> outputs,
        long failedRetiredResources)
    {
        if (engineState == MediaForgeEngineState.Failed) return MediaForgeRuntimeHealthStatus.Failed;
        if (engineState is MediaForgeEngineState.Idle or MediaForgeEngineState.Loaded or MediaForgeEngineState.Disposed)
            return MediaForgeRuntimeHealthStatus.Stopped;
        if (recoveries.Any(static state => state.Status == FaultRecoveryStatus.Recovering))
            return MediaForgeRuntimeHealthStatus.Recovering;
        if (recoveries.Any(static state => state.Status == FaultRecoveryStatus.Exhausted) ||
            outputs.Any(static output => output.Status == EncodedOutputRuntimeStatus.Failed) || failedRetiredResources > 0)
            return MediaForgeRuntimeHealthStatus.Degraded;
        return MediaForgeRuntimeHealthStatus.Healthy;
    }
}

internal sealed class EngineRecoveryCoordinator
{
    private readonly Dictionary<string, Task> _active = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool TryStart(string key, Func<Task> start, Action<Task> completed)
    {
        Task task;
        lock (_gate)
        {
            if (_active.ContainsKey(key)) return false;
            task = start();
            _active.Add(key, task);
        }

        _ = task.ContinueWith(
            value =>
            {
                lock (_gate) _active.Remove(key);
                completed(value);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    public Task[] Snapshot()
    {
        lock (_gate) return _active.Values.ToArray();
    }
}
