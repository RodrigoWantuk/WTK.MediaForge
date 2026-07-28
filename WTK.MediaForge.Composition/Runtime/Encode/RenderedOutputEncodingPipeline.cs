using System.Threading.Channels;
using System.Runtime.ExceptionServices;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal enum RenderedOutputEncodingRuntimeStatus
{
    Stopped,
    Starting,
    Running,
    Backpressure,
    Failed
}

internal sealed class RenderedOutputEncodingPipeline : IRenderedOutputFrameConsumer, IAsyncDisposable
{
    private readonly Dictionary<RenderOutputId, EncodingOutputRuntime> _outputs = [];
    private readonly object _gate = new();
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

    public RenderedOutputEncodingPipeline(IMediaForgeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics;

    public int OutputCount
    {
        get
        {
            lock (_gate)
                return _outputs.Count;
        }
    }

    public void RegisterOutput(
        RenderOutputId outputId,
        RenderedOutputEncodeFrameAdapter frameAdapter,
        EncodeSchedulerTarget schedulerTarget,
        HardwareEncoderInputRequirement inputRequirement,
        IMediaTransportAuditSink auditSink,
        int queueCapacity = 2,
        EncodedOutputBackpressurePolicy? backpressurePolicy = null)
    {
        if (outputId.IsEmpty)
            throw new ArgumentException("Output id cannot be empty.", nameof(outputId));

        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frameAdapter);
        ArgumentNullException.ThrowIfNull(schedulerTarget);
        ArgumentNullException.ThrowIfNull(inputRequirement);
        ArgumentNullException.ThrowIfNull(auditSink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_outputs.ContainsKey(outputId))
                throw new InvalidOperationException($"Rendered output {outputId} is already registered for encoding.");

            _outputs.Add(
                outputId,
                new EncodingOutputRuntime(
                    outputId,
                    frameAdapter,
                    schedulerTarget,
                    inputRequirement,
                    auditSink,
                    _diagnostics,
                    queueCapacity,
                    backpressurePolicy ?? EncodedOutputBackpressurePolicy.Diagnostics()));
        }
    }

    public bool TryGetStatus(
        RenderOutputId outputId,
        out RenderedOutputEncodingRuntimeStatus status)
    {
        lock (_gate)
        {
            if (_outputs.TryGetValue(outputId, out var runtime))
            {
                status = runtime.Status;
                return true;
            }
        }

        status = RenderedOutputEncodingRuntimeStatus.Stopped;
        return false;
    }

    public bool TryGetSnapshot(
        RenderOutputId outputId,
        out EncodedOutputRuntimeSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_outputs.TryGetValue(outputId, out var runtime))
            {
                snapshot = runtime.GetSnapshot();
                return true;
            }
        }

        snapshot = new EncodedOutputRuntimeSnapshot(
            outputId,
            EncodedOutputRuntimeStatus.Stopped,
            null,
            0,
            0,
            0,
            0,
            TimeSpan.Zero);
        return false;
    }

    public void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch) =>
        PublishCompletedFrames(frameBatch, encodedOutputDispatchIds: null);

    public void PublishCompletedFrames(
        RenderedOutputFrameBatch frameBatch,
        IReadOnlySet<RenderOutputId>? encodedOutputDispatchIds)
    {
        ArgumentNullException.ThrowIfNull(frameBatch);

        EncodingOutputRuntime[] runtimes;
        lock (_gate)
        {
            if (_disposed || _outputs.Count == 0)
                return;

            runtimes = _outputs.Values.ToArray();
        }

        foreach (var frame in frameBatch.Frames)
        {
            if (encodedOutputDispatchIds is not null && !encodedOutputDispatchIds.Contains(frame.OutputId))
                continue;

            var runtime = runtimes.FirstOrDefault(item => item.OutputId == frame.OutputId);
            if (runtime is null)
                continue;

            runtime.Enqueue(frameBatch, frame);
        }
    }

    public async ValueTask StopOutputAsync(
        RenderOutputId outputId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        EncodingOutputRuntime? runtime;
        lock (_gate)
        {
            if (!_outputs.Remove(outputId, out runtime))
                return;
        }

        await runtime.StopAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        EncodingOutputRuntime[] runtimes;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            runtimes = _outputs.Values.ToArray();
            _outputs.Clear();
        }

        List<Exception>? errors = null;
        foreach (var runtime in runtimes)
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose one or more rendered output encoding runtimes.", errors);
    }

    private sealed class EncodingOutputRuntime : IAsyncDisposable
    {
        private static readonly RenderOutputSinkId EncodingSinkId =
            RenderOutputSinkId.From(Guid.Parse("1C7244D8-6C8B-44C7-B0B0-4AB1F74F1E50"));

        private readonly RenderedOutputEncodeFrameAdapter _frameAdapter;
        private readonly EncodeSchedulerTarget _schedulerTarget;
        private readonly HardwareEncoderInputRequirement _inputRequirement;
        private readonly IMediaTransportAuditSink _auditSink;
        private readonly IMediaForgeDiagnosticsSink? _diagnostics;
        private readonly EncodedOutputBackpressurePolicy _backpressurePolicy;
        private readonly Channel<EncodingWorkItem> _queue;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private int _disposed;
        private int _fatalFailure;
        private long _framesSubmitted;
        private long _framesDropped;
        private volatile RenderedOutputEncodingRuntimeStatus _status;
        private string? _statusReason;

        public EncodingOutputRuntime(
            RenderOutputId outputId,
            RenderedOutputEncodeFrameAdapter frameAdapter,
            EncodeSchedulerTarget schedulerTarget,
            HardwareEncoderInputRequirement inputRequirement,
            IMediaTransportAuditSink auditSink,
            IMediaForgeDiagnosticsSink? diagnostics,
            int queueCapacity,
            EncodedOutputBackpressurePolicy backpressurePolicy)
        {
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Encoding queue capacity must be positive.");

            OutputId = outputId;
            _frameAdapter = frameAdapter;
            _schedulerTarget = schedulerTarget;
            _inputRequirement = inputRequirement;
            _auditSink = auditSink;
            _diagnostics = diagnostics;
            _backpressurePolicy = backpressurePolicy ?? throw new ArgumentNullException(nameof(backpressurePolicy));
            _status = RenderedOutputEncodingRuntimeStatus.Starting;
            _queue = Channel.CreateBounded<EncodingWorkItem>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
            _worker = Task.Run(ProcessAsync);
            _status = RenderedOutputEncodingRuntimeStatus.Running;
        }

        public RenderOutputId OutputId { get; }

        public RenderedOutputEncodingRuntimeStatus Status => _status;

        public EncodedOutputRuntimeSnapshot GetSnapshot()
        {
            var pipelineStatus = MapStatus(_status);
            var schedulerStatus = _schedulerTarget.Status;
            var status = schedulerStatus == EncodedOutputRuntimeStatus.Failed ||
                         pipelineStatus == EncodedOutputRuntimeStatus.Failed
                ? EncodedOutputRuntimeStatus.Failed
                : schedulerStatus == EncodedOutputRuntimeStatus.Backpressure ||
                  pipelineStatus == EncodedOutputRuntimeStatus.Backpressure
                    ? EncodedOutputRuntimeStatus.Backpressure
                    : pipelineStatus;

            return new EncodedOutputRuntimeSnapshot(
                OutputId,
                status,
                _schedulerTarget.StatusReason ?? Volatile.Read(ref _statusReason),
                Interlocked.Read(ref _framesSubmitted),
                _schedulerTarget.PacketsProduced,
                0,
                Interlocked.Read(ref _framesDropped) + _schedulerTarget.FramesDropped,
                _schedulerTarget.LastPacketLatency);
        }

        public void Enqueue(
            RenderedOutputFrameBatch frameBatch,
            RenderedOutputFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            if (_status == RenderedOutputEncodingRuntimeStatus.Failed ||
                TryFailFromSchedulerStatus())
            {
                return;
            }

            var lease = frameBatch.CreateLease(
                frame,
                new RenderOutputFrameInfo(
                    frame.OutputId,
                    EncodingSinkId,
                    frameBatch.FrameContext.FrameId,
                    frameBatch.FrameContext.PresentationTime,
                    frame.Size,
                    frame.Format,
                    frame.BackendKind));

            var item = new EncodingWorkItem(lease, frameBatch.FrameContext);
            if (_queue.Writer.TryWrite(item))
            {
                Interlocked.Increment(ref _framesSubmitted);
                return;
            }

            ReportBackpressureDrop();
            _ = DisposeLeaseAfterRejectedEnqueueAsync(lease);
        }

        public async ValueTask StopAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout), "Encoding runtime stop timeout must be positive.");

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _queue.Writer.TryComplete();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            Exception? shutdownFailure = null;
            try
            {
                await _worker.WaitAsync(linked.Token).ConfigureAwait(false);
                await _schedulerTarget.StopAsync(timeout, cancellationToken).ConfigureAwait(false);
                await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                _status = RenderedOutputEncodingRuntimeStatus.Stopped;
            }
            catch (OperationCanceledException ex) when (
                timeoutCts.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                shutdownFailure = new TimeoutException(
                    $"Rendered output encoding runtime for output {OutputId} did not stop within {timeout}.",
                    ex);
            }
            catch (Exception ex)
            {
                shutdownFailure = ex;
            }

            if (shutdownFailure is null)
            {
                _stop.Dispose();
                return;
            }

            _status = RenderedOutputEncodingRuntimeStatus.Failed;
            var cleanupErrors = new List<Exception>();
            await _stop.CancelAsync().ConfigureAwait(false);
            if (!_worker.IsCompleted)
            {
                try
                {
                    await _worker.WaitAsync(timeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    cleanupErrors.Add(cleanupException);
                }
            }

            if (_worker.IsCompleted)
            {
                await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                _stop.Dispose();
            }

            if (cleanupErrors.Count > 0)
            {
                cleanupErrors.Insert(0, shutdownFailure);
                throw new AggregateException(
                    $"Rendered output encoding runtime for output {OutputId} failed to stop safely.",
                    cleanupErrors);
            }

            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
        }

        public ValueTask DisposeAsync() =>
            StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
                    if (IsFatalFailure)
                    {
                        await DisposeWorkItemAsync(item).ConfigureAwait(false);
                        continue;
                    }

                    ScheduledRenderedFrame? scheduledFrame = null;
                    try
                    {
                        scheduledFrame = await _frameAdapter
                            .CreateScheduledFrameAsync(
                                item.Lease,
                                item.Context,
                                _inputRequirement,
                                _auditSink,
                                _stop.Token)
                            .ConfigureAwait(false);

                        if (IsFatalFailure)
                        {
                            scheduledFrame.Dispose();
                            scheduledFrame = null;
                            await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                            break;
                        }

                        _schedulerTarget.EnqueueRenderedFrame(scheduledFrame);
                        scheduledFrame = null;
                        if (TryFailFromSchedulerStatus())
                        {
                            await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                            break;
                        }

                        if (_status != RenderedOutputEncodingRuntimeStatus.Failed)
                        {
                            _status = RenderedOutputEncodingRuntimeStatus.Running;
                            Volatile.Write(ref _statusReason, null);
                        }
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                    {
                        scheduledFrame?.Dispose();
                        break;
                    }
                    catch (Exception ex)
                    {
                        scheduledFrame?.Dispose();
                        SetFailed(ex.Message);
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "engine.encoding_pipeline_frame_failed",
                            $"Rendered output {OutputId} could not be exported or scheduled for hardware encoding.",
                            nameof(RenderedOutputEncodingPipeline),
                            ex,
                            frameNumber: item.Context.FrameId,
                            outputId: OutputId.Value);

                        if (!_backpressurePolicy.AllowFrameDrop)
                        {
                            SetFatalFailure(ex.Message);
                            await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private void ReportBackpressureDrop()
        {
            Interlocked.Increment(ref _framesDropped);
            if (_backpressurePolicy.AllowFrameDrop)
            {
                _status = RenderedOutputEncodingRuntimeStatus.Backpressure;
                Volatile.Write(ref _statusReason, "Encoding export queue is full; live output policy dropped a frame.");
            }
            else
            {
                var reason = "Encoding export queue is full; recording output does not allow frame drops.";
                SetFatalFailure(reason);
            }

            MediaForgeDiagnostics.Report(
                _diagnostics,
                _backpressurePolicy.AllowFrameDrop
                    ? MediaForgeDiagnosticSeverity.Warning
                    : MediaForgeDiagnosticSeverity.Error,
                _backpressurePolicy.AllowFrameDrop
                    ? "engine.encoding_pipeline_frame_dropped_backpressure"
                    : "engine.encoding_pipeline_backpressure_failed",
                _backpressurePolicy.AllowFrameDrop
                    ? $"Rendered output {OutputId} dropped an encoding frame because the export queue is full."
                    : $"Rendered output {OutputId} failed because the encoding export queue is full.",
                nameof(RenderedOutputEncodingPipeline),
                outputId: OutputId.Value);
        }

        private bool IsFatalFailure => Volatile.Read(ref _fatalFailure) != 0;

        private bool TryFailFromSchedulerStatus()
        {
            if (_schedulerTarget.Status != EncodedOutputRuntimeStatus.Failed)
                return false;

            var reason = _schedulerTarget.StatusReason ?? "Hardware encode scheduler failed.";
            if (SetFatalFailure(reason))
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encoding_pipeline_scheduler_failed",
                    $"Rendered output {OutputId} stopped accepting frames because the hardware encode scheduler failed.",
                    nameof(RenderedOutputEncodingPipeline),
                    outputId: OutputId.Value);
            }

            return true;
        }

        private void SetFailed(string reason)
        {
            _status = RenderedOutputEncodingRuntimeStatus.Failed;
            Volatile.Write(ref _statusReason, reason);
        }

        private bool SetFatalFailure(string reason)
        {
            var first = Interlocked.Exchange(ref _fatalFailure, 1) == 0;
            if (first)
                _queue.Writer.TryComplete();

            SetFailed(reason);
            return first;
        }

        private static EncodedOutputRuntimeStatus MapStatus(RenderedOutputEncodingRuntimeStatus status) =>
            status switch
            {
                RenderedOutputEncodingRuntimeStatus.Starting => EncodedOutputRuntimeStatus.Starting,
                RenderedOutputEncodingRuntimeStatus.Running => EncodedOutputRuntimeStatus.Running,
                RenderedOutputEncodingRuntimeStatus.Backpressure => EncodedOutputRuntimeStatus.Backpressure,
                RenderedOutputEncodingRuntimeStatus.Failed => EncodedOutputRuntimeStatus.Failed,
                _ => EncodedOutputRuntimeStatus.Stopped
            };

        private async Task DisposeLeaseAfterRejectedEnqueueAsync(RenderOutputFrameLease lease)
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
                    "engine.encoding_pipeline_rejected_frame_dispose_failed",
                    $"Rendered output {OutputId} failed to release a rejected encoding frame lease.",
                    nameof(RenderedOutputEncodingPipeline),
                    ex,
                    outputId: OutputId.Value);
            }
        }

        private async ValueTask DisposeQueuedWorkItemsAsync()
        {
            while (_queue.Reader.TryRead(out var item))
                await DisposeWorkItemAsync(item).ConfigureAwait(false);
        }

        private async ValueTask DisposeWorkItemAsync(EncodingWorkItem item)
        {
            try
            {
                await item.Lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encoding_pipeline_queued_frame_dispose_failed",
                    $"Rendered output {OutputId} failed to release a queued encoding frame lease during shutdown.",
                    nameof(RenderedOutputEncodingPipeline),
                    ex,
                    frameNumber: item.Context.FrameId,
                    outputId: OutputId.Value);
            }
        }
    }

    private sealed record EncodingWorkItem(
        RenderOutputFrameLease Lease,
        FrameExecutionContext Context);
}
