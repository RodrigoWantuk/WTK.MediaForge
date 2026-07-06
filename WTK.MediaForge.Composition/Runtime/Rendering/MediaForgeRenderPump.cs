using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Compatibility wrapper over <see cref="Scheduling.FrameScheduler"/>.
/// </summary>
internal sealed class MediaForgeRenderPump : IAsyncDisposable
{
    private readonly Scheduling.FrameScheduler _scheduler;

    public MediaForgeRenderPump(
        double framesPerSecond,
        Func<bool> canPublish,
        Action publish,
        IMediaForgeDiagnosticsSink? diagnostics)
        : this(
            framesPerSecond,
            canPublish,
            _ =>
            {
                publish();
            },
            static () => Array.Empty<Core.Identifiers.RenderOutputId>(),
            diagnostics)
    {
    }

    internal MediaForgeRenderPump(
        double framesPerSecond,
        Func<bool> canPublish,
        Action<Scheduling.FrameExecutionContext> publish,
        Func<IReadOnlyList<Core.Identifiers.RenderOutputId>> targetOutputs,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        _scheduler = new Scheduling.FrameScheduler(
            framesPerSecond,
            canPublish,
            publish,
            targetOutputs,
            diagnostics);
    }

    internal Scheduling.FrameScheduler Scheduler => _scheduler;

    public bool IsRunning => _scheduler.IsRunning;

    public void RequestFrame() => _scheduler.RequestFrame();

    public ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _scheduler.StopAsync(timeout, cancellationToken);

    public ValueTask DisposeAsync() => _scheduler.DisposeAsync();
}
