using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class OutputRouteTransitionRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<RenderOutputId, ActiveTransition> _active = [];
    private bool _disposed;

    internal event EventHandler<OutputRouteTransitionPhaseChangedEventArgs>? PhaseChanged;

    public void BeginTransition(
        RenderOutputId outputId,
        OutputRouteTransition transition,
        CanvasId fromCanvasId,
        CanvasId toCanvasId)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var active = new ActiveTransition
        {
            Transition = transition,
            FromCanvasId = fromCanvasId,
            ToCanvasId = toCanvasId,
            PreviousVersionGraph = new SceneVersionGraph(fromCanvasId, new Dictionary<CanvasId, SceneVersionId>()),
            CurrentVersionGraph = new SceneVersionGraph(toCanvasId, new Dictionary<CanvasId, SceneVersionId>()),
            Elapsed = TimeSpan.Zero,
            Progress = transition.Kind == OutputRouteTransitionKind.Cut ? 1f : 0f
        };

        ReplaceActiveTransition(outputId, active);
    }

    internal void BeginSceneVersionTransition(
        RenderOutputId outputId,
        OutputRouteTransition transition,
        SceneVersionGraph previousVersionGraph,
        SceneVersionGraph currentVersionGraph,
        ProjectStateSnapshot previousProjectState,
        IDisposable versionOwnership)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(previousVersionGraph);
        ArgumentNullException.ThrowIfNull(currentVersionGraph);
        ArgumentNullException.ThrowIfNull(previousProjectState);
        ArgumentNullException.ThrowIfNull(versionOwnership);

        var active = new ActiveTransition
        {
            Transition = transition,
            FromCanvasId = previousVersionGraph.RootCanvasId,
            ToCanvasId = currentVersionGraph.RootCanvasId,
            PreviousVersionGraph = previousVersionGraph,
            CurrentVersionGraph = currentVersionGraph,
            PreviousProjectState = previousProjectState,
            VersionOwnership = versionOwnership,
            Elapsed = TimeSpan.Zero,
            Progress = transition.Kind == OutputRouteTransitionKind.Cut ? 1f : 0f
        };

        ReplaceActiveTransition(outputId, active);
    }

    internal Guid BeginSceneRouteTransition(
        Guid operationId,
        RenderOutputId outputId,
        OutputRouteTransition transition,
        SceneVersionGraph previousVersionGraph,
        SceneVersionGraph currentVersionGraph,
        ProjectStateSnapshot previousProjectState,
        ProjectStateSnapshot destinationProjectState,
        IDisposable versionOwnership)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(previousVersionGraph);
        ArgumentNullException.ThrowIfNull(currentVersionGraph);
        ArgumentNullException.ThrowIfNull(previousProjectState);
        ArgumentNullException.ThrowIfNull(destinationProjectState);
        ArgumentNullException.ThrowIfNull(versionOwnership);
        if (operationId == Guid.Empty)
            throw new ArgumentException("A non-empty transition operation id is required.", nameof(operationId));

        var active = new ActiveTransition
        {
            OperationId = operationId,
            Transition = transition,
            FromCanvasId = previousVersionGraph.RootCanvasId,
            ToCanvasId = currentVersionGraph.RootCanvasId,
            PreviousVersionGraph = previousVersionGraph,
            CurrentVersionGraph = currentVersionGraph,
            PreviousProjectState = previousProjectState,
            DestinationProjectState = destinationProjectState,
            VersionOwnership = versionOwnership,
            Elapsed = TimeSpan.Zero,
            Progress = transition.Kind == OutputRouteTransitionKind.Cut ? 1f : 0f
        };

        ReplaceActiveTransition(outputId, active);
        RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.Started);
        if (transition.Kind == OutputRouteTransitionKind.Cut)
        {
            RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.SwitchPointReached);
            Complete(outputId, active);
        }

        return active.OperationId;
    }

    public bool TryGetProgress(RenderOutputId outputId, out float progress)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(outputId, out var active))
            {
                progress = active.Progress;
                return true;
            }
        }

        progress = 0f;
        return false;
    }

    internal bool TryGetTransition(RenderOutputId outputId, out OutputRouteTransitionRuntimeState state)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(outputId, out var active))
            {
                state = new OutputRouteTransitionRuntimeState(
                    active.Transition,
                    active.FromCanvasId,
                    active.ToCanvasId,
                    active.PreviousVersionGraph,
                    active.CurrentVersionGraph,
                    active.PreviousProjectState,
                    active.DestinationProjectState,
                    active.Progress);
                return true;
            }
        }

        state = default;
        return false;
    }

    public void Advance(RenderOutputId outputId, TimeSpan deltaTime)
    {
        ActiveTransition? active;
        var reachedSwitchPoint = false;
        var completed = false;
        lock (_gate)
        {
            if (!_active.TryGetValue(outputId, out active) || active.Transition.Kind == OutputRouteTransitionKind.Cut)
                return;

            var duration = TimeSpan.FromMilliseconds(Math.Max(active.Transition.DurationMs, 1));
            var previousProgress = active.Progress;
            active.Elapsed += deltaTime < TimeSpan.Zero ? TimeSpan.Zero : deltaTime;
            active.Progress = Math.Clamp((float)(active.Elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0f, 1f);
            reachedSwitchPoint = !active.SwitchPointRaised && previousProgress < 0.5f && active.Progress >= 0.5f;
            if (reachedSwitchPoint)
                active.SwitchPointRaised = true;
            completed = active.Progress >= 1f && _active.Remove(outputId);
        }

        if (reachedSwitchPoint)
            RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.SwitchPointReached);
        if (completed)
        {
            RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.Completed);
            active.Dispose();
        }
    }

    internal void AdvanceAll(TimeSpan deltaTime)
    {
        RenderOutputId[] outputIds;
        lock (_gate)
            outputIds = _active.Keys.ToArray();
        foreach (var outputId in outputIds)
            Advance(outputId, deltaTime);
    }

    internal bool Cancel(RenderOutputId outputId, Guid operationId)
    {
        ActiveTransition? active;
        lock (_gate)
        {
            if (!_active.TryGetValue(outputId, out active) || active.OperationId != operationId)
                return false;
            _active.Remove(outputId);
        }

        RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.Cancelled);
        active.Dispose();
        return true;
    }

    internal bool Fail(RenderOutputId outputId, Guid operationId, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ActiveTransition? active;
        lock (_gate)
        {
            if (!_active.TryGetValue(outputId, out active) || active.OperationId != operationId)
                return false;
            active.Failure = failure;
            _active.Remove(outputId);
        }

        RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.Failed);
        active.Dispose();
        return true;
    }

    internal void Clear()
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
        DisposeActiveTransitions();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        DisposeActiveTransitions();
    }

    private void ReplaceActiveTransition(RenderOutputId outputId, ActiveTransition active)
    {
        ActiveTransition? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active.Remove(outputId, out previous);
            _active.Add(outputId, active);
        }

        if (previous is not null)
        {
            RaisePhaseChanged(outputId, previous, OutputRouteTransitionPhase.Cancelled);
            previous.Dispose();
        }
    }

    private void Complete(RenderOutputId outputId, ActiveTransition active)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(outputId, out var current) || !ReferenceEquals(current, active))
                return;
            _active.Remove(outputId);
        }

        RaisePhaseChanged(outputId, active, OutputRouteTransitionPhase.Completed);
        active.Dispose();
    }

    private void DisposeActiveTransitions()
    {
        KeyValuePair<RenderOutputId, ActiveTransition>[] activeTransitions;
        lock (_gate)
        {
            activeTransitions = _active.ToArray();
            _active.Clear();
        }

        List<Exception>? errors = null;
        foreach (var pair in activeTransitions)
        {
            try
            {
                RaisePhaseChanged(pair.Key, pair.Value, OutputRouteTransitionPhase.Cancelled);
                pair.Value.Dispose();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to release scene version transition ownership.", errors);
    }

    private void RaisePhaseChanged(RenderOutputId outputId, ActiveTransition active, OutputRouteTransitionPhase phase) =>
        PhaseChanged?.Invoke(
            this,
            new OutputRouteTransitionPhaseChangedEventArgs(
                active.OperationId,
                outputId,
                active.FromCanvasId,
                active.ToCanvasId,
                phase,
                active.Progress,
                active.Failure));

    private sealed class ActiveTransition : IDisposable
    {
        private IDisposable? _versionOwnership;

        public Guid OperationId { get; init; } = Guid.NewGuid();
        public required OutputRouteTransition Transition { get; init; }
        public required CanvasId FromCanvasId { get; init; }
        public required CanvasId ToCanvasId { get; init; }
        public required SceneVersionGraph PreviousVersionGraph { get; init; }
        public required SceneVersionGraph CurrentVersionGraph { get; init; }
        public ProjectStateSnapshot? PreviousProjectState { get; init; }
        public ProjectStateSnapshot? DestinationProjectState { get; init; }
        public IDisposable? VersionOwnership { init => _versionOwnership = value; }
        public TimeSpan Elapsed { get; set; }
        public float Progress { get; set; }
        public bool SwitchPointRaised { get; set; }
        public Exception? Failure { get; set; }

        public void Dispose() => Interlocked.Exchange(ref _versionOwnership, null)?.Dispose();
    }
}

internal readonly record struct OutputRouteTransitionRuntimeState(
    OutputRouteTransition Transition,
    CanvasId FromCanvasId,
    CanvasId ToCanvasId,
    SceneVersionGraph PreviousVersionGraph,
    SceneVersionGraph CurrentVersionGraph,
    ProjectStateSnapshot? PreviousProjectState,
    ProjectStateSnapshot? DestinationProjectState,
    float Progress);

internal enum OutputRouteTransitionPhase
{
    Started,
    SwitchPointReached,
    Completed,
    Cancelled,
    Failed
}

internal sealed record OutputRouteTransitionPhaseChangedEventArgs(
    Guid OperationId,
    RenderOutputId OutputId,
    CanvasId FromCanvasId,
    CanvasId ToCanvasId,
    OutputRouteTransitionPhase Phase,
    float Progress,
    Exception? Failure);
