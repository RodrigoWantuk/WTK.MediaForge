using System.Diagnostics;
using System.Runtime.ExceptionServices;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using PublicRenderOutputSink = WTK.MediaForge.Composition.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

internal sealed class RenderOutputSinkDispatcher : IAsyncDisposable
{
    private const int DefaultQueueCapacity = 2;
    private static readonly TimeSpan DefaultSinkStopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<RenderOutputId, List<SinkRegistration>> _registrations = [];
    private readonly Dictionary<RenderOutputId, long> _frameNumbers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Action? _beforeDeliveryEnqueue;
    private readonly Action? _beforeAvailabilitySignal;
    private bool _disposed;

    private TimeSpan _sinkStopTimeout;

    public RenderOutputSinkDispatcher(
        IMediaForgeDiagnosticsSink? diagnostics = null,
        TimeSpan? sinkStopTimeout = null,
        Action? beforeDeliveryEnqueue = null,
        Action? beforeAvailabilitySignal = null)
    {
        _diagnostics = diagnostics;
        _beforeDeliveryEnqueue = beforeDeliveryEnqueue;
        _beforeAvailabilitySignal = beforeAvailabilitySignal;
        SinkStopTimeout = sinkStopTimeout ?? DefaultSinkStopTimeout;
    }

    public TimeSpan SinkStopTimeout
    {
        get => _sinkStopTimeout;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Sink stop timeout must be positive.");

            _sinkStopTimeout = value;
        }
    }

    public int SinkCount
    {
        get
        {
            lock (_gate)
                return _registrations.Values.Sum(static registrations => registrations.Count);
        }
    }

    public bool HasSinks(RenderOutputId outputId)
    {
        lock (_gate)
        {
            return _registrations.TryGetValue(outputId, out var registrations) &&
                   registrations.Count > 0;
        }
    }

    public bool IsSinkAttached(RenderOutputId outputId, RenderOutputSinkId sinkId)
    {
        lock (_gate)
        {
            return _registrations.TryGetValue(outputId, out var registrations) &&
                   registrations.Any(registration => registration.Sink.Id == sinkId);
        }
    }

    public async Task AttachAsync(
        MediaForgeRenderOutput output,
        PublicRenderOutputSink sink,
        TimeSpan startTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sink);
        if (startTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startTimeout), "Sink start timeout must be positive.");

        cancellationToken.ThrowIfCancellationRequested();

        if (sink.Id.IsEmpty)
            throw new ArgumentException("Render output sink id cannot be empty.", nameof(sink));

        var context = new RenderOutputSinkContext(
            output.Id,
            output.OutputSize,
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Vulkan);

        SinkRegistration? registration = null;
        var reserved = false;
        var startAttempted = false;

        try
        {
            registration = new SinkRegistration(
                output.Id,
                sink,
                DefaultQueueCapacity,
                _diagnostics,
                _beforeAvailabilitySignal);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureCanAttachSinkLocked(sink);

                var registrations = GetOrCreateRegistrationsLocked(output.Id);
                registrations.Add(registration);
                reserved = true;
            }

            startAttempted = true;
            await AwaitSinkStartAsync(sink, context, startTimeout, cancellationToken).ConfigureAwait(false);
            registration.Start();
        }
        catch (Exception ex)
        {
            if (reserved && registration is not null)
                RemoveRegistration(registration);

            try
            {
                if (startAttempted)
                    await CleanupFailedAttachAsync(sink, ex).ConfigureAwait(false);
            }
            finally
            {
                registration?.DisposeUnstarted();
            }

            ExceptionDispatchInfo.Capture(ex).Throw();
            throw;
        }
    }

    public async Task<bool> DetachAsync(
        RenderOutputId outputId,
        RenderOutputSinkId sinkId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SinkRegistration? registration = null;

        lock (_gate)
        {
            if (!_registrations.TryGetValue(outputId, out var registrations))
                return false;

            registration = registrations.FirstOrDefault(item => item.Sink.Id == sinkId);
            if (registration is null)
                return false;
        }

        try
        {
            await StopRegistrationAsync(registration, cancellationToken).ConfigureAwait(false);
            RemoveRegistration(registration);
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "sink.detach_failed",
                $"Failed to detach render output sink {sinkId}.",
                nameof(RenderOutputSinkDispatcher),
                ex);
            throw;
        }

        return true;
    }

    public async Task DetachAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<SinkRegistration> registrations;

        lock (_gate)
        {
            registrations = _registrations.Values.SelectMany(static item => item).ToList();
            _registrations.Clear();
            _frameNumbers.Clear();
        }

        List<Exception>? errors = null;

        foreach (var registration in registrations)
        {
            try
            {
                await StopRegistrationAsync(registration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "sink.detach_failed",
                    $"Failed to detach render output sink {registration.Sink.Id}.",
                    nameof(RenderOutputSinkDispatcher),
                    ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to detach one or more render output sinks.", errors);
    }

    public void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch)
    {
        ArgumentNullException.ThrowIfNull(frameBatch);

        List<(SinkRegistration Registration, RenderOutputFrameLease Lease)> deliveries = [];

        lock (_gate)
        {
            if (_disposed)
                return;

            foreach (var frame in frameBatch.Frames)
            {
                if (!_registrations.TryGetValue(frame.OutputId, out var registrations) ||
                    registrations.Count == 0)
                {
                    continue;
                }

                var frameNumber = NextFrameNumberLocked(frame.OutputId);
                var timestamp = _clock.Elapsed;

                foreach (var registration in registrations.Where(static registration => registration.IsActive))
                {
                    var info = new RenderOutputFrameInfo(
                        frame.OutputId,
                        registration.Sink.Id,
                        frameNumber,
                        timestamp,
                        frame.Size,
                        frame.Format,
                        frame.BackendKind);

                    deliveries.Add((registration, frameBatch.CreateLease(frame, info)));
                }
            }
        }

        foreach (var (registration, lease) in deliveries)
        {
            try
            {
                _beforeDeliveryEnqueue?.Invoke();
                if (!registration.TryEnqueue(lease))
                    DisposeUndeliveredFrame(registration, lease);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "sink.enqueue_failed",
                    $"Failed to enqueue frame for output {registration.OutputId} and sink {registration.Sink.Id}.",
                    nameof(RenderOutputSinkDispatcher),
                    ex);
                DisposeUndeliveredFrame(registration, lease);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<SinkRegistration> registrations;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            registrations = _registrations.Values.SelectMany(static item => item).ToList();
            _registrations.Clear();
            _frameNumbers.Clear();
        }

        List<Exception>? errors = null;

        foreach (var registration in registrations)
        {
            try
            {
                await StopRegistrationAsync(registration, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "sink.dispose_failed",
                    $"Failed to dispose render output sink {registration.Sink.Id}.",
                    nameof(RenderOutputSinkDispatcher),
                    ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose render output sink dispatcher.", errors);
    }

    private List<SinkRegistration> GetOrCreateRegistrationsLocked(RenderOutputId outputId)
    {
        if (!_registrations.TryGetValue(outputId, out var registrations))
        {
            registrations = [];
            _registrations[outputId] = registrations;
        }

        return registrations;
    }

    private long NextFrameNumberLocked(RenderOutputId outputId)
    {
        _frameNumbers.TryGetValue(outputId, out var current);
        var next = current + 1;
        _frameNumbers[outputId] = next;
        return next;
    }

    private void EnsureCanAttachSinkLocked(PublicRenderOutputSink sink)
    {
        foreach (var registration in _registrations.Values.SelectMany(static item => item))
        {
            if (ReferenceEquals(registration.Sink, sink))
            {
                throw new InvalidOperationException(
                    $"Render output sink instance {sink.Id} is already attached to output {registration.OutputId}.");
            }

            if (registration.Sink.Id == sink.Id)
            {
                throw new InvalidOperationException(
                    $"Render output sink id {sink.Id} is already attached to output {registration.OutputId}.");
            }
        }
    }

    private void RemoveRegistration(SinkRegistration registration)
    {
        lock (_gate)
        {
            if (!_registrations.TryGetValue(registration.OutputId, out var registrations))
                return;

            registrations.Remove(registration);
            if (registrations.Count == 0)
                _registrations.Remove(registration.OutputId);
        }
    }

    private async ValueTask CleanupFailedAttachAsync(
        PublicRenderOutputSink sink,
        Exception attachException)
    {
        try
        {
            await StopAndDisposeSinkAsync(sink, SinkStopTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            var timeout = IsTimeoutFailure(cleanupException);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                timeout ? "sink.attach_cleanup_timeout" : "sink.attach_cleanup_failed",
                timeout
                    ? $"Render output sink {sink.Id} did not clean up within {SinkStopTimeout} after attach failed."
                    : $"Render output sink {sink.Id} failed while cleaning up after attach failed.",
                nameof(RenderOutputSinkDispatcher),
                cleanupException);

            throw new AggregateException(
                $"Render output sink {sink.Id} failed to attach and cleanup did not complete successfully.",
                attachException,
                cleanupException);
        }
    }

    private static async ValueTask AwaitSinkStartAsync(
        PublicRenderOutputSink sink,
        RenderOutputSinkContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startTask = sink.StartAsync(context, timeoutCts.Token).AsTask();

        try
        {
            await startTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            timeoutCts.Cancel();
            throw new TimeoutException($"Render output sink {sink.Id} did not start within {timeout}.", ex);
        }
        catch (OperationCanceledException ex) when (
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            timeoutCts.Cancel();
            throw new TimeoutException($"Render output sink {sink.Id} did not start within {timeout}.", ex);
        }
    }

    private static async ValueTask StopAndDisposeSinkAsync(
        PublicRenderOutputSink sink,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Sink cleanup timeout must be positive.");

        var deadline = CreateDeadline(timeout);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        List<Exception>? errors = null;
        try
        {
            var stopTask = sink.StopAsync(linked.Token).AsTask();
            await stopTask
                .WaitAsync(GetRemainingTime(deadline), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (
            timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Render output sink {sink.Id} cleanup stop timed out.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Render output sink {sink.Id} cleanup stop timed out.", ex);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            var disposeTask = sink.DisposeAsync().AsTask();
            await disposeTask
                .WaitAsync(GetRemainingTime(deadline), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Render output sink {sink.Id} cleanup dispose timed out.", ex);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (errors is not null)
            throw new AggregateException($"Render output sink {sink.Id} cleanup failed.", errors);
    }

    private async ValueTask StopRegistrationAsync(
        SinkRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            await registration.StopAsync(SinkStopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "sink.stop_timeout",
                $"Render output sink {registration.Sink.Id} did not stop within {SinkStopTimeout}.",
                nameof(RenderOutputSinkDispatcher),
                ex);
            throw;
        }
    }

    private void DisposeUndeliveredFrame(
        SinkRegistration registration,
        RenderOutputFrameLease lease)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "sink.undelivered_frame_dispose_failed",
                    $"Failed to release undelivered frame for output {registration.OutputId} from sink {registration.Sink.Id}.",
                    nameof(RenderOutputSinkDispatcher),
                    ex);
            }
        });
    }

    private static long CreateDeadline(TimeSpan timeout) =>
        Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);

    private static TimeSpan GetRemainingTime(long deadline)
    {
        var remainingTicks = deadline - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
    }

    private static bool IsTimeoutFailure(Exception exception) =>
        exception is TimeoutException ||
        exception is AggregateException aggregate &&
        aggregate.InnerExceptions.Any(IsTimeoutFailure);

    private sealed class SinkRegistration : IAsyncDisposable
    {
        private readonly RenderOutputSinkQueue _queue;
        private readonly SemaphoreSlim _available = new(0);
        private readonly CancellationTokenSource _stop = new();
        private readonly IMediaForgeDiagnosticsSink? _diagnostics;
        private readonly Action? _beforeAvailabilitySignal;
        private Task? _worker;
        private Task? _stopTask;
        private CancellationTokenSource? _stopTimeout;
        private int _stopTimedOut;
        private int _active;
        private int _disposed;

        public SinkRegistration(
            RenderOutputId outputId,
            PublicRenderOutputSink sink,
            int capacity,
            IMediaForgeDiagnosticsSink? diagnostics,
            Action? beforeAvailabilitySignal)
        {
            OutputId = outputId;
            Sink = sink;
            _diagnostics = diagnostics;
            _beforeAvailabilitySignal = beforeAvailabilitySignal;
            _queue = new RenderOutputSinkQueue(
                capacity,
                sink.BackpressureMode);
        }

        public RenderOutputId OutputId { get; }

        public PublicRenderOutputSink Sink { get; }

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Start()
        {
            Volatile.Write(ref _active, 1);
            _worker = Task.Run(ProcessAsync);
        }

        public bool TryEnqueue(RenderOutputFrameLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);

            if (!IsActive)
                return false;

            try
            {
                var result = _queue.TryEnqueue(lease, out var releaseLease);

                if (releaseLease is not null)
                {
                    if (result is RenderOutputSinkQueueEnqueueResult.ReplacedPendingOldReturnedToCaller or
                        RenderOutputSinkQueueEnqueueResult.DroppedIncomingReturnedToCaller)
                    {
                        ReportBackpressureDrop();
                    }

                    DisposeDroppedFrame(releaseLease);
                }

                if (result == RenderOutputSinkQueueEnqueueResult.EnqueuedAndWorkerSignaled)
                {
                    try
                    {
                        _beforeAvailabilitySignal?.Invoke();
                        _available.Release();
                    }
                    catch (Exception ex)
                    {
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "sink.enqueue_signal_failed",
                            $"Failed to signal frame availability for output {OutputId} and sink {Sink.Id}.",
                            nameof(RenderOutputSinkDispatcher),
                            ex);
                        StopAfterSignalFailure();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "sink.enqueue_failed",
                    $"Failed to enqueue frame for output {OutputId} and sink {Sink.Id}.",
                    nameof(RenderOutputSinkDispatcher),
                    ex);
                return false;
            }
        }

        private void StopAfterSignalFailure()
        {
            Volatile.Write(ref _active, 0);
            _queue.StopAccepting();

            foreach (var queued in _queue.Drain())
                DisposeDroppedFrame(queued);
        }

        public async ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Sink stop timeout must be positive.");

            var stopTask = EnsureStopStarted(timeout, cancellationToken);

            try
            {
                await stopTask.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (AggregateException ex) when (
                Volatile.Read(ref _stopTimedOut) != 0 &&
                !cancellationToken.IsCancellationRequested &&
                ex.InnerExceptions.Any(static inner => inner is OperationCanceledException))
            {
                throw new TimeoutException(
                    $"Render output sink {Sink.Id} did not stop within {timeout}.",
                    ex);
            }
            catch (OperationCanceledException ex) when (
                _stopTimeout?.IsCancellationRequested == true &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Render output sink {Sink.Id} did not stop within {timeout}.",
                    ex);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"Render output sink {Sink.Id} did not stop within {timeout}.",
                    ex);
            }
        }

        public ValueTask DisposeAsync() => StopAsync(DefaultSinkStopTimeout, CancellationToken.None);

        public void DisposeUnstarted()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Volatile.Write(ref _active, 0);
            _available.Dispose();
            _stop.Dispose();
        }

        private Task EnsureStopStarted(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Volatile.Write(ref _active, 0);
                _queue.StopAccepting();
                _stop.Cancel();
                _available.Release();

                _stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _stopTimeout.CancelAfter(timeout);
                _stopTask = StopCoreAsync(_stopTimeout.Token);
            }

            return _stopTask ?? Task.CompletedTask;
        }

        private async Task StopCoreAsync(CancellationToken cancellationToken)
        {
            List<Exception>? errors = null;
            try
            {
                await Sink.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                (errors ??= []).Add(ex);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            if (_worker is not null)
            {
                try
                {
                    await _worker.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            try
            {
                await DisposeQueuedFramesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await Sink.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                    Volatile.Write(ref _stopTimedOut, 1);

                _available.Dispose();
                _stop.Dispose();
                _stopTimeout?.Dispose();
                _stopTimeout = null;
            }

            if (errors is not null)
                throw new AggregateException($"Failed to stop render output sink {Sink.Id}.", errors);
        }

        private async Task ProcessAsync()
        {
            while (true)
            {
                try
                {
                    await _available.WaitAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }

                RenderOutputFrameLease? lease = null;

                if (!_queue.TryDequeue(out lease) && !_queue.IsAccepting)
                    return;

                if (lease is null)
                    continue;

                await DeliverAsync(lease).ConfigureAwait(false);
            }
        }

        private async ValueTask DeliverAsync(RenderOutputFrameLease lease)
        {
            await using (lease.ConfigureAwait(false))
            {
                try
                {
                    await Sink.OnFrameAsync(lease, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "sink.frame_delivery_failed",
                        $"Render output sink {Sink.Id} failed while consuming a frame.",
                        nameof(RenderOutputSinkDispatcher),
                        ex);
                }
            }
        }

        private async ValueTask DisposeQueuedFramesAsync()
        {
            foreach (var lease in _queue.Drain())
                await DisposeDroppedFrameAsync(lease).ConfigureAwait(false);
        }

        private void ReportBackpressureDrop()
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Warning,
                "sink.frame_dropped_backpressure",
                $"Frame for output {OutputId} was dropped because sink {Sink.Id} is backpressured.",
                nameof(RenderOutputSinkDispatcher));
        }

        private void DisposeDroppedFrame(RenderOutputFrameLease lease)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await DisposeDroppedFrameAsync(lease).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "sink.dropped_frame_dispose_failed",
                        $"Failed to release dropped frame for output {OutputId} from sink {Sink.Id}.",
                        nameof(RenderOutputSinkDispatcher),
                        ex);
                }
            });
        }

        private static async ValueTask DisposeDroppedFrameAsync(RenderOutputFrameLease lease) =>
            await lease.DisposeAsync().ConfigureAwait(false);
    }
}
