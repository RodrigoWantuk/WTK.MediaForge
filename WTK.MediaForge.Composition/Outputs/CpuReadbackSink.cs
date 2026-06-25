using WTK.MediaForge.Composition.Runtime.Rendering;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// Diagnostic/sample sink that copies completed output frames into owned CPU buffers.
/// Not a production preview or encoder path.
/// </summary>
public sealed class CpuReadbackSink : IRenderOutputSink
{
    private readonly Func<CpuReadbackFrame, CancellationToken, ValueTask>? _onFrame;
    private readonly double? _maxFramesPerSecond;
    private long _nextReadbackDeadlineTicks;
    private int _started;

    public CpuReadbackSink(
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<CpuReadbackFrame, CancellationToken, ValueTask>? onFrame = null,
        double? maxFramesPerSecond = null)
        : this(RenderOutputSinkId.New(), backpressureMode, onFrame, maxFramesPerSecond)
    {
    }

    public CpuReadbackSink(
        RenderOutputSinkId id,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<CpuReadbackFrame, CancellationToken, ValueTask>? onFrame = null,
        double? maxFramesPerSecond = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Sink id cannot be empty.", nameof(id));

        if (maxFramesPerSecond is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFramesPerSecond),
                "Max frames per second must be positive when specified.");
        }

        Id = id;
        BackpressureMode = backpressureMode;
        _onFrame = onFrame;
        _maxFramesPerSecond = maxFramesPerSecond;
    }

    public RenderOutputSinkId Id { get; }

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.CpuReadback;

    public RenderOutputSinkBackpressureMode BackpressureMode { get; }

    public double? MaxFramesPerSecond => _maxFramesPerSecond;

    public event EventHandler<CpuReadbackFrameEventArgs>? FrameReady;

    public ValueTask StartAsync(
        RenderOutputSinkContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _started, 1);
        Volatile.Write(ref _nextReadbackDeadlineTicks, 0);
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

        if (ShouldSkipReadbackForRateLimit())
        {
            await frame.DisposeAsync().ConfigureAwait(false);
            return;
        }

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

    private bool ShouldSkipReadbackForRateLimit()
    {
        if (_maxFramesPerSecond is not { } maxFramesPerSecond)
            return false;

        var intervalMs = 1000d / maxFramesPerSecond;
        var now = Environment.TickCount64;
        var nextDeadline = Volatile.Read(ref _nextReadbackDeadlineTicks);

        if (now < nextDeadline)
            return true;

        Volatile.Write(ref _nextReadbackDeadlineTicks, now + (long)Math.Ceiling(intervalMs));
        return false;
    }
}
