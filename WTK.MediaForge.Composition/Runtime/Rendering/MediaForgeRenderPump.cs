using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class MediaForgeRenderPump : IAsyncDisposable
{
    private readonly Func<bool> _canPublish;
    private readonly Action _publish;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _interval;
    private readonly Task _loop;
    private int _disposed;

    public MediaForgeRenderPump(
        double framesPerSecond,
        Func<bool> canPublish,
        Action publish,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Frame rate must be finite and positive.");

        _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _diagnostics = diagnostics;
        _interval = TimeSpan.FromSeconds(1d / framesPerSecond);
        _loop = Task.Run(RunAsync);
    }

    public bool IsRunning => !_loop.IsCompleted;

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
            throw new TimeoutException("Render pump did not stop within the expected timeout.", ex);
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
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Warning,
                    "engine.render_pump_frame_dropped_backpressure",
                    "Render pump skipped a frame because the render thread is backpressured.",
                    nameof(MediaForgeRenderPump));
                continue;
            }

            try
            {
                _publish();
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
                    "engine.render_pump_publish_failed",
                    "Render pump failed to publish a frame.",
                    nameof(MediaForgeRenderPump),
                    ex);
            }
        }
    }

    private async Task WaitForNextTickAsync()
    {
        var delay = Task.Delay(_interval, _stop.Token);
        var wake = _wake.WaitAsync(_stop.Token);
        var completed = await Task.WhenAny(delay, wake).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
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
