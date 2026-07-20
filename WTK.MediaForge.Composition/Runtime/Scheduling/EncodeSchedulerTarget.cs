using System.Diagnostics;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal enum EncodeSchedulerBackpressurePolicy
{
    KeepLatest,
    QueueWithBackpressure
}

internal sealed class ScheduledRenderedFrame : IDisposable
{
    private int _disposed;

    public required FrameExecutionContext Context { get; init; }

    public GpuTextureLease? TextureLease { get; init; }

    public HardwareEncoderInputLease? EncoderInputLease { get; init; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        TextureLease?.Dispose();
        EncoderInputLease?.Dispose();
    }
}

/// <summary>
/// Scheduler target for hardware encode. Encode pacing is independent from render pacing.
/// </summary>
internal sealed class EncodeSchedulerTarget : IAsyncDisposable
{
    private readonly IHardwareVideoEncoder _encoder;
    private readonly IGpuFrameExporter _frameExporter;
    private readonly IMediaTransportAuditSink _auditSink;
    private readonly Action<EncodedVideoPacket> _onPacketProduced;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Queue<ScheduledRenderedFrame> _pendingFrames = new();
    private readonly object _queueGate = new();
    private readonly SemaphoreSlim _queueSignal = new(0, 1);
    private readonly CancellationTokenSource _abort = new();
    private readonly Task _encodeLoop;
    private readonly TimeSpan _encodeTimeout;
    private readonly int _queueCapacity;
    private readonly EncodeSchedulerBackpressurePolicy _backpressurePolicy;
    private readonly RenderOutputId _outputId;
    private int _queueSignalSet;
    private int _disposed;
    private int _stopRequested;
    private int _acceptingFrames = 1;
    private int _fatalFailure;
    private long _framesSubmitted;
    private long _framesDropped;
    private long _packetsProduced;
    private long _lastPacketLatencyTicks;
    private volatile EncodedOutputRuntimeStatus _status = EncodedOutputRuntimeStatus.Starting;
    private string? _statusReason;

    public EncodeSchedulerTarget(
        IHardwareVideoEncoder encoder,
        IGpuFrameExporter frameExporter,
        IMediaTransportAuditSink auditSink,
        Action<EncodedVideoPacket> onPacketProduced,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        int queueCapacity = 2,
        EncodeSchedulerBackpressurePolicy backpressurePolicy = EncodeSchedulerBackpressurePolicy.KeepLatest,
        TimeSpan? encodeTimeout = null,
        RenderOutputId outputId = default)
    {
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Encode queue capacity must be positive.");

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _frameExporter = frameExporter ?? throw new ArgumentNullException(nameof(frameExporter));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _onPacketProduced = onPacketProduced ?? throw new ArgumentNullException(nameof(onPacketProduced));
        _diagnostics = diagnostics;
        _queueCapacity = queueCapacity;
        _backpressurePolicy = backpressurePolicy;
        _outputId = outputId;
        _encodeTimeout = encodeTimeout ?? TimeSpan.FromSeconds(2);
        _encodeLoop = Task.Run(ProcessEncodeQueueAsync);
        _status = EncodedOutputRuntimeStatus.Running;
    }

    public int PendingFrameCount
    {
        get
        {
            lock (_queueGate)
                return _pendingFrames.Count;
        }
    }

    public EncodedOutputRuntimeStatus Status => _status;

    public string? StatusReason => Volatile.Read(ref _statusReason);

    public long FramesSubmitted => Interlocked.Read(ref _framesSubmitted);

    public long FramesDropped => Interlocked.Read(ref _framesDropped);

    public long PacketsProduced => Interlocked.Read(ref _packetsProduced);

    public TimeSpan LastPacketLatency => TimeSpan.FromTicks(Interlocked.Read(ref _lastPacketLatencyTicks));

    public void OnRenderedFrame(ScheduledRenderedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.Context);

        if ((frame.TextureLease is null) == (frame.EncoderInputLease is null))
        {
            frame.Dispose();
            throw new ArgumentException(
                "A scheduled rendered frame must provide exactly one GPU texture lease or pre-exported encoder input lease.",
                nameof(frame));
        }

        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _acceptingFrames) == 0)
        {
            frame.Dispose();
            return;
        }

        if (_status == EncodedOutputRuntimeStatus.Failed)
        {
            frame.Dispose();
            return;
        }

        var dropped = 0;
        var enqueued = false;
        List<ScheduledRenderedFrame>? droppedFrames = null;
        string? failedReason = null;

        lock (_queueGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                AddDropped(frame);
            }
            else if (_pendingFrames.Count >= _queueCapacity)
            {
                if (_backpressurePolicy == EncodeSchedulerBackpressurePolicy.KeepLatest)
                {
                    while (_pendingFrames.Count >= _queueCapacity)
                    {
                        AddDropped(_pendingFrames.Dequeue());
                        dropped++;
                    }

                    _pendingFrames.Enqueue(frame);
                    enqueued = true;
                }
                else
                {
                    AddDropped(frame);
                    dropped = 1;
                    failedReason = "Encode queue is full and this route does not allow frame drops.";
                }
            }
            else
            {
                _pendingFrames.Enqueue(frame);
                enqueued = true;
            }
        }

        if (droppedFrames is not null)
        {
            foreach (var droppedFrame in droppedFrames)
                droppedFrame.Dispose();
        }

        if (dropped > 0)
            ReportFrameDroppedBackpressure(dropped);

        if (enqueued)
        {
            Interlocked.Increment(ref _framesSubmitted);
            SignalQueue();
        }

        if (failedReason is not null)
            SetFailed(failedReason);

        void AddDropped(ScheduledRenderedFrame droppedFrame)
        {
            droppedFrames ??= [];
            droppedFrames.Add(droppedFrame);
        }
    }

    public void EnqueueRenderedFrame(ScheduledRenderedFrame frame) => OnRenderedFrame(frame);

    public async ValueTask StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Volatile.Write(ref _acceptingFrames, 0);
        Volatile.Write(ref _stopRequested, 1);
        SignalQueue();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _encodeLoop.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            await _abort.CancelAsync().ConfigureAwait(false);
            SignalQueue();
            throw new TimeoutException("Encode scheduler target did not stop within the expected timeout.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _abort.CancelAsync().ConfigureAwait(false);
            SignalQueue();
            throw;
        }
        finally
        {
            if (_encodeLoop.IsCompleted)
            {
                ClearPendingFrames();
                _queueSignal.Dispose();
                _abort.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync() => StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

    private async Task ProcessEncodeQueueAsync()
    {
        while (!_abort.IsCancellationRequested)
        {
            if (_status == EncodedOutputRuntimeStatus.Failed)
            {
                ClearPendingFrames();
                break;
            }

            if (!TryDequeue(out var scheduledFrame))
            {
                if (Volatile.Read(ref _stopRequested) != 0)
                    break;

                try
                {
                    await _queueSignal.WaitAsync(_abort.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _queueSignalSet, 0);
                }
                catch (OperationCanceledException) when (_abort.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            using var renderedFrame = scheduledFrame!;
            var frameContext = renderedFrame.Context;

            try
            {
                using var timeoutCts = new CancellationTokenSource(_encodeTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token, timeoutCts.Token);
                var started = Stopwatch.GetTimestamp();

                var encodeContext = new HardwareEncodeFrameContext
                {
                    FrameId = frameContext.FrameId,
                    PresentationTime = frameContext.PresentationTime,
                    FrameBudget = frameContext.FrameBudget,
                    CancellationToken = linked.Token
                };

                var packet = renderedFrame.EncoderInputLease is not null
                    ? await _encoder
                        .EncodeAsync(
                            new EncodeFrameContext
                            {
                                InputLease = renderedFrame.EncoderInputLease,
                                FrameNumber = frameContext.FrameId,
                                PresentationTime = frameContext.PresentationTime,
                                CancellationToken = linked.Token
                            },
                            _auditSink)
                        .ConfigureAwait(false)
                    : await _encoder
                        .SubmitFrameAsync(renderedFrame.TextureLease!, encodeContext, _frameExporter, _auditSink)
                        .ConfigureAwait(false);

                if (packet is not null)
                {
                    _onPacketProduced(packet);
                    ReportPacketProduced(frameContext.FrameId, Stopwatch.GetElapsedTime(started));
                }
            }
            catch (OperationCanceledException) when (_abort.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Warning,
                    "engine.encode_scheduler_frame_timeout",
                    "Encode scheduler frame timed out before the encoder completed.",
                    nameof(EncodeSchedulerTarget),
                    ex,
                    frameNumber: frameContext.FrameId,
                    outputId: ToDiagnosticOutputId());
                SetFailed("Hardware encode timed out.");
                break;
            }
            catch (InvalidOperationException ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encode_scheduler_encoder_unavailable",
                    "Encode scheduler could not use the configured encoder or GPU exporter.",
                    nameof(EncodeSchedulerTarget),
                    ex,
                    frameNumber: frameContext.FrameId,
                    outputId: ToDiagnosticOutputId());
                SetFailed(ex.Message);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encode_scheduler_target_failed",
                    "Encode scheduler target failed to produce an encoded packet.",
                    nameof(EncodeSchedulerTarget),
                    ex,
                    frameNumber: frameContext.FrameId,
                    outputId: ToDiagnosticOutputId());
                SetFailed(ex.Message);
                break;
            }
        }

        if (!_abort.IsCancellationRequested && _status != EncodedOutputRuntimeStatus.Failed)
        {
            try
            {
                var drainedPackets = await _encoder
                    .DrainAsync(_auditSink, _abort.Token)
                    .ConfigureAwait(false);
                foreach (var packet in drainedPackets)
                {
                    _onPacketProduced(packet);
                    ReportPacketProduced(0, TimeSpan.Zero);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetFailed($"Hardware encoder finalization failed: {ex.Message}");
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encode_scheduler_drain_failed",
                    "Hardware encoder failed while draining delayed packets.",
                    nameof(EncodeSchedulerTarget),
                    ex,
                    outputId: ToDiagnosticOutputId());
                throw;
            }
        }
    }

    private bool TryDequeue(out ScheduledRenderedFrame? frame)
    {
        lock (_queueGate)
            return _pendingFrames.TryDequeue(out frame);
    }

    private void ClearPendingFrames()
    {
        ScheduledRenderedFrame[] pendingFrames;

        lock (_queueGate)
        {
            pendingFrames = _pendingFrames.ToArray();
            _pendingFrames.Clear();
        }

        foreach (var pendingFrame in pendingFrames)
            pendingFrame.Dispose();
    }

    private void SignalQueue()
    {
        if (Interlocked.Exchange(ref _queueSignalSet, 1) != 0)
            return;

        try
        {
            _queueSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReportFrameDroppedBackpressure(int dropped)
    {
        Interlocked.Add(ref _framesDropped, dropped);
        if (_backpressurePolicy == EncodeSchedulerBackpressurePolicy.KeepLatest &&
            _status != EncodedOutputRuntimeStatus.Failed)
        {
            _status = EncodedOutputRuntimeStatus.Backpressure;
            Volatile.Write(ref _statusReason, "Encode queue dropped frame(s) because live output policy keeps the latest frame.");
        }

        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "engine.encode_scheduler_frame_dropped_backpressure",
            $"Encode scheduler dropped {dropped} frame(s) because the encode queue is full.",
            nameof(EncodeSchedulerTarget),
            outputId: ToDiagnosticOutputId());
    }

    private void ReportPacketProduced(long frameId, TimeSpan latency)
    {
        Interlocked.Increment(ref _packetsProduced);
        Interlocked.Exchange(ref _lastPacketLatencyTicks, latency.Ticks);
        if (_status != EncodedOutputRuntimeStatus.Failed)
        {
            _status = EncodedOutputRuntimeStatus.Running;
            Volatile.Write(ref _statusReason, null);
        }

        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Info,
            "engine.encode_scheduler_packet_produced",
            "Encode scheduler produced an encoded packet.",
            nameof(EncodeSchedulerTarget),
            frameNumber: frameId,
            outputId: ToDiagnosticOutputId());
    }

    private void SetFailed(string reason)
    {
        _status = EncodedOutputRuntimeStatus.Failed;
        Volatile.Write(ref _statusReason, reason);

        if (Interlocked.Exchange(ref _fatalFailure, 1) == 0)
        {
            ClearPendingFrames();
            SignalQueue();
        }
    }

    private Guid? ToDiagnosticOutputId() => _outputId.IsEmpty ? null : _outputId.Value;
}
