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
            StartedUtc = DateTime.UtcNow,
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
        var elapsed = DateTime.UtcNow - active.StartedUtc + deltaTime;
        active.Progress = Math.Clamp((float)(elapsed.TotalMilliseconds / duration.TotalMilliseconds), 0f, 1f);

        if (active.Progress >= 1f)
            _active.Remove(outputId);
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

        public required DateTime StartedUtc { get; init; }

        public float Progress { get; set; }
    }
}
