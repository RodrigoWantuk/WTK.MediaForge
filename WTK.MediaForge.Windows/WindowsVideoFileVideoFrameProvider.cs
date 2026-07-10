using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsVideoFileVideoFrameProvider : IVideoFrameProvider, IDisposable
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultNoFrameDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan DefaultFrameDelay = TimeSpan.FromMilliseconds(16);

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SourceFrameBuffer _buffer = new(new MediaSourceBufferOptions
    {
        Mode = MediaSourceBufferMode.TimelineDriven,
        Capacity = 2
    });
    private readonly VideoSourceRuntime _runtime;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly DecodedFrameToSourceFrameAdapter _adapter;
    private CancellationTokenSource? _decodeCancellation;
    private Task? _decodeTask;
    private Exception? _lastError;
    private long _frameNumber;
    private int _state = (int)MediaSourceState.Stopped;
    private int _disposed;

    public WindowsVideoFileVideoFrameProvider(
        SourceId id,
        string name,
        VideoSourceRuntime runtime,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        DecodedFrameToSourceFrameAdapter? adapter = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Source id cannot be empty.", nameof(id));

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Video file" : name;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _diagnostics = diagnostics;
        _adapter = adapter ?? DecodedFrameToSourceFrameAdapter.Instance;
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State => (MediaSourceState)Volatile.Read(ref _state);

    public Exception? LastError => Volatile.Read(ref _lastError);

    internal TimeSpan StopTimeout { get; set; } = DefaultStopTimeout;

    internal TimeSpan NoFrameDelay { get; set; } = DefaultNoFrameDelay;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (State is MediaSourceState.Running or MediaSourceState.Starting)
                return;

            SetState(MediaSourceState.Starting);
            Volatile.Write(ref _lastError, null);

            try
            {
                await _runtime.OpenAsync(cancellationToken).ConfigureAwait(false);
                _runtime.Play();

                _decodeCancellation = new CancellationTokenSource();
                _decodeTask = Task.Run(
                    () => DecodeLoopAsync(_decodeCancellation.Token),
                    CancellationToken.None);

                SetState(MediaSourceState.Running);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex);
                SetState(MediaSourceState.Failed);
                await StopRuntimeAfterStartFailureAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is MediaSourceState.Stopped && _decodeTask is null)
                return;

            SetState(MediaSourceState.Stopping);

            var decodeCancellation = _decodeCancellation;
            var decodeTask = _decodeTask;
            _decodeCancellation = null;
            _decodeTask = null;

            if (decodeCancellation is not null)
                await StopDecodeTaskAsync(decodeCancellation, decodeTask, cancellationToken).ConfigureAwait(false);

            ClearBufferedFrames();
            await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);

            if (State != MediaSourceState.Failed)
                SetState(MediaSourceState.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        lease = null!;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return State == MediaSourceState.Running &&
               _buffer.TryAcquireForRender(TimeSpan.Zero, out lease);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            StopAsync(CancellationToken.None)
                .WaitAsync(StopTimeout)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.video_file.dispose_failed",
                $"Video file source '{Name}' failed to dispose cleanly.",
                nameof(WindowsVideoFileVideoFrameProvider),
                ex,
                Id.Value,
                Name);
        }
        finally
        {
            _buffer.Dispose();
            _runtime.Dispose();
            _decodeCancellation?.Dispose();
            _lifecycleGate.Dispose();
        }
    }

    private async Task DecodeLoopAsync(CancellationToken cancellationToken)
    {
        var audit = new CollectingMediaTransportAuditSink();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var decoded = await _runtime
                    .TryDecodeNextFrameAsync(audit, cancellationToken)
                    .ConfigureAwait(false);

                if (decoded is null)
                {
                    await Task.Delay(NoFrameDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var delay = decoded.Duration > TimeSpan.Zero
                    ? decoded.Duration
                    : DefaultFrameDelay;

                GpuFrameLease? sourceLease = null;
                try
                {
                    sourceLease = _adapter.CreateSourceFrameLease(
                        decoded,
                        Id,
                        Interlocked.Increment(ref _frameNumber));
                    _buffer.Publish(sourceLease);
                    sourceLease = null;
                }
                finally
                {
                    sourceLease?.Dispose();
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _lastError, ex);
                SetState(MediaSourceState.Failed);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.video_file.decode_failed",
                    $"Video file source '{Name}' failed while decoding a GPU frame.",
                    nameof(WindowsVideoFileVideoFrameProvider),
                    ex,
                    Id.Value,
                    Name,
                    Volatile.Read(ref _frameNumber));
                return;
            }
        }
    }

    private async Task StopDecodeTaskAsync(
        CancellationTokenSource decodeCancellation,
        Task? decodeTask,
        CancellationToken cancellationToken)
    {
        try
        {
            decodeCancellation.Cancel();

            if (decodeTask is not null)
                await decodeTask.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            var timeout = new TimeoutException(
                $"Video file source '{Name}' decode worker did not stop within {StopTimeout}.",
                ex);
            Volatile.Write(ref _lastError, timeout);
            SetState(MediaSourceState.Failed);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.video_file.stop_timeout",
                timeout.Message,
                nameof(WindowsVideoFileVideoFrameProvider),
                timeout,
                Id.Value,
                Name);
            throw timeout;
        }
        finally
        {
            decodeCancellation.Dispose();
        }
    }

    private async Task StopRuntimeAfterStartFailureAsync()
    {
        try
        {
            await _runtime.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception stopEx)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.video_file_start_rollback_failed",
                $"Video file source '{Name}' failed to stop after start failure.",
                nameof(WindowsVideoFileVideoFrameProvider),
                stopEx,
                Id.Value,
                Name);
        }
    }

    private void ClearBufferedFrames()
    {
        while (_buffer.TryTakeLatestFrame(out var lease))
            lease.Dispose();
    }

    private void SetState(MediaSourceState state) =>
        Volatile.Write(ref _state, (int)state);
}
