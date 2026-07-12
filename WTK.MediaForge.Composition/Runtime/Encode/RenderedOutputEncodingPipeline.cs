using System.Threading.Channels;
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
        int queueCapacity = 2)
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
                    queueCapacity));
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

    public void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch)
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
        private readonly Channel<EncodingWorkItem> _queue;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private int _disposed;
        private volatile RenderedOutputEncodingRuntimeStatus _status;

        public EncodingOutputRuntime(
            RenderOutputId outputId,
            RenderedOutputEncodeFrameAdapter frameAdapter,
            EncodeSchedulerTarget schedulerTarget,
            HardwareEncoderInputRequirement inputRequirement,
            IMediaTransportAuditSink auditSink,
            IMediaForgeDiagnosticsSink? diagnostics,
            int queueCapacity)
        {
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Encoding queue capacity must be positive.");

            OutputId = outputId;
            _frameAdapter = frameAdapter;
            _schedulerTarget = schedulerTarget;
            _inputRequirement = inputRequirement;
            _auditSink = auditSink;
            _diagnostics = diagnostics;
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

        public void Enqueue(
            RenderedOutputFrameBatch frameBatch,
            RenderedOutputFrame frame)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

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
                return;

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
            await _stop.CancelAsync().ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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
                _status = RenderedOutputEncodingRuntimeStatus.Failed;
                throw new TimeoutException($"Rendered output encoding runtime for output {OutputId} did not stop within {timeout}.", ex);
            }
            catch
            {
                _status = RenderedOutputEncodingRuntimeStatus.Failed;
                throw;
            }
            finally
            {
                await DisposeQueuedWorkItemsAsync().ConfigureAwait(false);
                _stop.Dispose();
            }
        }

        public ValueTask DisposeAsync() =>
            StopAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
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

                        _schedulerTarget.EnqueueRenderedFrame(scheduledFrame);
                        scheduledFrame = null;
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                    {
                        scheduledFrame?.Dispose();
                        break;
                    }
                    catch (Exception ex)
                    {
                        scheduledFrame?.Dispose();
                        _status = RenderedOutputEncodingRuntimeStatus.Failed;
                        MediaForgeDiagnostics.Report(
                            _diagnostics,
                            MediaForgeDiagnosticSeverity.Error,
                            "engine.encoding_pipeline_frame_failed",
                            $"Rendered output {OutputId} could not be exported or scheduled for hardware encoding.",
                            nameof(RenderedOutputEncodingPipeline),
                            ex,
                            frameNumber: item.Context.FrameId);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private void ReportBackpressureDrop()
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Warning,
                "engine.encoding_pipeline_frame_dropped_backpressure",
                $"Rendered output {OutputId} dropped an encoding frame because the export queue is full.",
                nameof(RenderedOutputEncodingPipeline));
        }

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
                    ex);
            }
        }

        private async ValueTask DisposeQueuedWorkItemsAsync()
        {
            while (_queue.Reader.TryRead(out var item))
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
                        frameNumber: item.Context.FrameId);
                }
            }
        }
    }

    private sealed record EncodingWorkItem(
        RenderOutputFrameLease Lease,
        FrameExecutionContext Context);
}
