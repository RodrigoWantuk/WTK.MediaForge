using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class OutputRouteTransitionRuntime : IDisposable
{
    private readonly Dictionary<RenderOutputId, ActiveTransition> _active = new();
    private bool _disposed;

    public void BeginTransition(
        RenderOutputId outputId,
        OutputRouteTransition transition,
        CanvasId fromCanvasId,
        CanvasId toCanvasId)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _active[outputId] = new ActiveTransition
        {
            Transition = transition,
            FromCanvasId = fromCanvasId,
            ToCanvasId = toCanvasId,
            PreviousVersionGraph = new SceneVersionGraph(fromCanvasId, new Dictionary<CanvasId, SceneVersionId>()),
            CurrentVersionGraph = new SceneVersionGraph(toCanvasId, new Dictionary<CanvasId, SceneVersionId>()),
            PreviousProjectState = null,
            Elapsed = TimeSpan.Zero,
            Progress = transition.Kind == OutputRouteTransitionKind.Cut ? 1f : 0f
        };
    }

    internal void BeginSceneVersionTransition(
        RenderOutputId outputId,
        OutputRouteTransition transition,
        SceneVersionGraph previousVersionGraph,
        SceneVersionGraph currentVersionGraph,
        ProjectStateSnapshot previousProjectState)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(previousVersionGraph);
        ArgumentNullException.ThrowIfNull(currentVersionGraph);
        ArgumentNullException.ThrowIfNull(previousProjectState);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _active[outputId] = new ActiveTransition
        {
            Transition = transition,
            FromCanvasId = previousVersionGraph.RootCanvasId,
            ToCanvasId = currentVersionGraph.RootCanvasId,
            PreviousVersionGraph = previousVersionGraph,
            CurrentVersionGraph = currentVersionGraph,
            PreviousProjectState = previousProjectState,
            Elapsed = TimeSpan.Zero,
            Progress = transition.Kind == OutputRouteTransitionKind.Cut ? 1f : 0f
        };
    }

    public bool TryGetProgress(RenderOutputId outputId, out float progress)
    {
        if (_active.TryGetValue(outputId, out var active))
        {
            progress = active.Progress;
            return true;
        }

        progress = 0f;
        return false;
    }

    internal bool TryGetTransition(
        RenderOutputId outputId,
        out OutputRouteTransitionRuntimeState state)
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
                active.Progress);
            return true;
        }

        state = default;
        return false;
    }

    public void Advance(RenderOutputId outputId, TimeSpan deltaTime)
    {
        if (!_active.TryGetValue(outputId, out var active))
            return;

        if (active.Transition.Kind == OutputRouteTransitionKind.Cut)
        {
            active.Progress = 1f;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(Math.Max(active.Transition.DurationMs, 1));
        active.Elapsed += deltaTime < TimeSpan.Zero ? TimeSpan.Zero : deltaTime;
        active.Progress = Math.Clamp((float)(active.Elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0f, 1f);

        if (active.Progress >= 1f)
            _active.Remove(outputId);
    }

    internal void AdvanceAll(TimeSpan deltaTime)
    {
        var outputIds = _active.Keys.ToArray();
        foreach (var outputId in outputIds)
            Advance(outputId, deltaTime);
    }

    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _active.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _active.Clear();
    }

    private sealed class ActiveTransition
    {
        public required OutputRouteTransition Transition { get; init; }

        public required CanvasId FromCanvasId { get; init; }

        public required CanvasId ToCanvasId { get; init; }

        public required SceneVersionGraph PreviousVersionGraph { get; init; }

        public required SceneVersionGraph CurrentVersionGraph { get; init; }

        public required ProjectStateSnapshot? PreviousProjectState { get; init; }

        public TimeSpan Elapsed { get; set; }

        public float Progress { get; set; }
    }
}

internal readonly record struct OutputRouteTransitionRuntimeState(
    OutputRouteTransition Transition,
    CanvasId FromCanvasId,
    CanvasId ToCanvasId,
    SceneVersionGraph PreviousVersionGraph,
    SceneVersionGraph CurrentVersionGraph,
    ProjectStateSnapshot? PreviousProjectState,
    float Progress);
