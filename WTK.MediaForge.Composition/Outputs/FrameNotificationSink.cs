namespace WTK.MediaForge.Composition.Outputs;

public sealed class FrameNotificationSink : IRenderOutputSink
{
    private readonly Func<RenderOutputFrameInfo, CancellationToken, ValueTask>? _onFrame;
    private int _started;

    public FrameNotificationSink(
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameInfo, CancellationToken, ValueTask>? onFrame = null)
        : this(RenderOutputSinkId.New(), backpressureMode, onFrame)
    {
    }

    public FrameNotificationSink(
        RenderOutputSinkId id,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameInfo, CancellationToken, ValueTask>? onFrame = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Sink id cannot be empty.", nameof(id));

        Id = id;
        BackpressureMode = backpressureMode;
        _onFrame = onFrame;
    }

    public RenderOutputSinkId Id { get; }

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.FrameNotification;

    public RenderOutputSinkBackpressureMode BackpressureMode { get; }

    public event EventHandler<FrameNotificationEventArgs>? FrameReady;

    public ValueTask StartAsync(
        RenderOutputSinkContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _started, 1);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnFrameAsync(
        RenderOutputFrameLease frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _started) == 0)
            throw new InvalidOperationException("FrameNotificationSink must be started before receiving frames.");

        var info = frame.Info;
        FrameReady?.Invoke(this, new FrameNotificationEventArgs(info));

        if (_onFrame is not null)
            await _onFrame(info, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _started, 0);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _started, 0);
        return ValueTask.CompletedTask;
    }
}
