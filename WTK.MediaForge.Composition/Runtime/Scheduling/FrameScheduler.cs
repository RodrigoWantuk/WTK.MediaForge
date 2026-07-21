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
    private readonly AutoResetEvent _wake = new(false);
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _frameBudget;
    private readonly Thread _thread;
    private long _frameId;
    private long _presentationTimeTicks;
    private long _lastBackpressureDiagnosticTicks;
    private int _backpressureDropCount;
    private int _stopRequested;
    private int _resourcesDisposed;
    private FrameExecutionContext? _lastPublishedContext;

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
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "MediaForge.FrameScheduler"
        };
        _thread.Start();
    }

    public TimeSpan FrameBudget => _frameBudget;

    public bool IsRunning => !_completion.Task.IsCompleted;

    internal FrameExecutionContext? LastPublishedContext => Volatile.Read(ref _lastPublishedContext);

    public void RequestFrame()
    {
        if (Volatile.Read(ref _stopRequested) != 0 || Volatile.Read(ref _resourcesDisposed) != 0)
            return;

        SafeSignalWake();
    }

    public async ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Frame scheduler stop timeout must be positive.");

        await _stopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _resourcesDisposed) != 0)
                return;

            Volatile.Write(ref _stopRequested, 1);
            SafeSignalWake();

            try
            {
                await _completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    "Frame scheduler did not stop within the expected timeout. Cleanup ownership is retained and StopAsync may be retried.",
                    ex);
            }
            finally
            {
                if (_completion.Task.IsCompleted)
                    CompleteStop();
            }
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public ValueTask DisposeAsync() => StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

    private void Run()
    {
        try
        {
            while (Volatile.Read(ref _stopRequested) == 0)
            {
                _wake.WaitOne(_frameBudget);
                while (_wake.WaitOne(TimeSpan.Zero))
                {
                }

                if (Volatile.Read(ref _stopRequested) != 0)
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
                        PresentationTime = AdvancePresentationTime(),
                        FrameBudget = _frameBudget,
                        TargetOutputs = _targetOutputs()
                    };

                    Volatile.Write(ref _lastPublishedContext, context);
                    _publish(context);
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _stopRequested) != 0)
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

            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "engine.frame_scheduler_loop_failed",
                "Frame scheduler loop terminated unexpectedly.",
                nameof(FrameScheduler),
                ex);
            _completion.TrySetException(ex);
        }
    }

    private TimeSpan AdvancePresentationTime()
    {
        var ticks = Interlocked.Add(ref _presentationTimeTicks, _frameBudget.Ticks);
        return TimeSpan.FromTicks(ticks);
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

    private void CompleteStop()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

        _wake.Dispose();
    }

    private void SafeSignalWake()
    {
        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
