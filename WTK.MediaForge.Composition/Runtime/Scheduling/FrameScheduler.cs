using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal sealed class FrameScheduler : IAsyncDisposable
{
    private static readonly TimeSpan BackpressureDiagnosticInterval = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _canPublish;
    private readonly Action<FrameExecutionContext> _publish;
    private readonly Func<IReadOnlyList<RenderOutputId>> _targetOutputs;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _frameBudget;
    private readonly Task _loop;
    private long _frameId;
    private long _lastBackpressureDiagnosticTicks;
    private int _backpressureDropCount;
    private int _disposed;

    public FrameScheduler(
        double framesPerSecond,
        Func<bool> canPublish,
        Action<FrameExecutionContext> publish,
        Func<IReadOnlyList<RenderOutputId>> targetOutputs,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frame rate must be finite and positive.");

        _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _targetOutputs = targetOutputs ?? throw new ArgumentNullException(nameof(targetOutputs));
        _diagnostics = diagnostics;
        _frameBudget = TimeSpan.FromSeconds(1d / framesPerSecond);
        _loop = Task.Run(RunAsync);
    }

    public TimeSpan FrameBudget => _frameBudget;

    public bool IsRunning => !_loop.IsCompleted;

    internal FrameExecutionContext? LastPublishedContext { get; private set; }

    public void RequestFrame()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stop.Cancel();
        SafeReleaseWake();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await _loop.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("Frame scheduler did not stop within the expected timeout.", ex);
        }
        finally
        {
            _wake.Dispose();
            _stop.Dispose();
        }
    }

    public ValueTask DisposeAsync() => StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await WaitForNextTickAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }

            if (_stop.IsCancellationRequested)
                break;

            if (!_canPublish())
            {
                ReportBackpressureDrop();
                continue;
            }

            try
            {
                var context = new FrameExecutionContext
                {
                    FrameId = Interlocked.Increment(ref _frameId),
                    Timestamp = DateTimeOffset.UtcNow,
                    FrameBudget = _frameBudget,
                    TargetOutputs = _targetOutputs()
                };

                LastPublishedContext = context;
                _publish(context);
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.frame_scheduler_publish_failed",
                    "Frame scheduler failed to publish a frame.",
                    nameof(FrameScheduler),
                    ex);
            }
        }
    }

    private async Task WaitForNextTickAsync()
    {
        _ = await _wake.WaitAsync(_frameBudget, _stop.Token).ConfigureAwait(false);

        while (_wake.Wait(0))
        {
        }
    }

    private void ReportBackpressureDrop()
    {
        var dropped = Interlocked.Increment(ref _backpressureDropCount);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastBackpressureDiagnosticTicks);
        if (last != 0 && now - last < BackpressureDiagnosticInterval.TotalMilliseconds)
            return;

        if (Interlocked.CompareExchange(ref _lastBackpressureDiagnosticTicks, now, last) != last)
            return;

        var reportedCount = Interlocked.Exchange(ref _backpressureDropCount, 0);
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "engine.frame_scheduler_frame_dropped_backpressure",
            $"Frame scheduler skipped {reportedCount} frame(s) because the render thread is backpressured.",
            nameof(FrameScheduler));
    }

    private void SafeReleaseWake()
    {
        try
        {
            _wake.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
