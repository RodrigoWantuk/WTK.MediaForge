using WTK.MediaForge.Composition.Runtime.Rendering;

namespace WTK.MediaForge.Composition.Outputs;

public sealed class CpuReadbackSink : IRenderOutputSink
{
    private readonly Func<CpuReadbackFrame, CancellationToken, ValueTask>? _onFrame;
    private int _started;

    public CpuReadbackSink(
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<CpuReadbackFrame, CancellationToken, ValueTask>? onFrame = null)
        : this(RenderOutputSinkId.New(), backpressureMode, onFrame)
    {
    }

    public CpuReadbackSink(
        RenderOutputSinkId id,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<CpuReadbackFrame, CancellationToken, ValueTask>? onFrame = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Sink id cannot be empty.", nameof(id));

        Id = id;
        BackpressureMode = backpressureMode;
        _onFrame = onFrame;
    }

    public RenderOutputSinkId Id { get; }

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.CpuReadback;

    public RenderOutputSinkBackpressureMode BackpressureMode { get; }

    public event EventHandler<CpuReadbackFrameEventArgs>? FrameReady;

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
            throw new InvalidOperationException("CpuReadbackSink must be started before receiving frames.");

        if (frame.SurfaceLease is not ICpuReadableRenderedOutputSurfaceLease readableSurface)
        {
            throw new NotSupportedException(
                $"Render backend '{frame.BackendKind}' does not expose CPU readback for output frames.");
        }

        var readback = await readableSurface
            .ReadCpuFrameAsync(frame.Info, cancellationToken)
            .ConfigureAwait(false);

        await frame.DisposeAsync().ConfigureAwait(false);

        FrameReady?.Invoke(this, new CpuReadbackFrameEventArgs(readback));

        if (_onFrame is not null)
            await _onFrame(readback, cancellationToken).ConfigureAwait(false);
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
