using System.Diagnostics;
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

    private readonly object _gate = new();
    private readonly Dictionary<RenderOutputId, List<SinkRegistration>> _registrations = [];
    private readonly Dictionary<RenderOutputId, long> _frameNumbers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

    public RenderOutputSinkDispatcher(IMediaForgeDiagnosticsSink? diagnostics = null) =>
        _diagnostics = diagnostics;

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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sink);
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
        var accepted = false;

        try
        {
            registration = new SinkRegistration(
                output.Id,
                sink,
                DefaultQueueCapacity,
                _diagnostics);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureCanAttachSinkLocked(sink);

                var registrations = GetOrCreateRegistrationsLocked(output.Id);
                registrations.Add(registration);
                reserved = true;
            }

            startAttempted = true;
            await sink.StartAsync(context, cancellationToken).ConfigureAwait(false);
            registration.Start();
            accepted = true;
        }
        finally
        {
            if (!accepted)
            {
                if (reserved && registration is not null)
                    RemoveRegistration(registration);

                if (startAttempted)
                    await StopAndDisposeSinkAsync(sink, cancellationToken).ConfigureAwait(false);

                registration?.DisposeUnstarted();
            }
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

            registrations.Remove(registration);
            if (registrations.Count == 0)
                _registrations.Remove(outputId);
        }

        try
        {
            await registration.StopAsync(cancellationToken).ConfigureAwait(false);
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
                await registration.StopAsync(cancellationToken).ConfigureAwait(false);
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
            registration.TryEnqueue(lease);
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
                await registration.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
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

    private static async ValueTask StopAndDisposeSinkAsync(
        PublicRenderOutputSink sink,
        CancellationToken cancellationToken)
    {
        try
        {
            await sink.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await sink.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class SinkRegistration : IAsyncDisposable
    {
        private readonly RenderOutputSinkQueue _queue;
        private readonly SemaphoreSlim _available = new(0);
        private readonly CancellationTokenSource _stop = new();
        private readonly IMediaForgeDiagnosticsSink? _diagnostics;
        private Task? _worker;
        private int _active;
        private int _disposed;

        public SinkRegistration(
            RenderOutputId outputId,
            PublicRenderOutputSink sink,
            int capacity,
            IMediaForgeDiagnosticsSink? diagnostics)
        {
            OutputId = outputId;
            Sink = sink;
            _diagnostics = diagnostics;
            _queue = new RenderOutputSinkQueue(
                capacity,
                sink.BackpressureMode,
                DropFrame);
        }

        public RenderOutputId OutputId { get; }

        public PublicRenderOutputSink Sink { get; }

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Start()
        {
            Volatile.Write(ref _active, 1);
            _worker = Task.Run(ProcessAsync);
        }

        public void TryEnqueue(RenderOutputFrameLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);

            if (_queue.TryEnqueue(lease))
                _available.Release();
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Volatile.Write(ref _active, 0);

            _queue.StopAccepting();

            _stop.Cancel();
            _available.Release();

            if (_worker is not null)
            {
                try
                {
                    await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                }
            }

            await DisposeQueuedFramesAsync().ConfigureAwait(false);

            try
            {
                await Sink.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await Sink.DisposeAsync().ConfigureAwait(false);
                _available.Dispose();
                _stop.Dispose();
            }
        }

        public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

        public void DisposeUnstarted()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Volatile.Write(ref _active, 0);
            _available.Dispose();
            _stop.Dispose();
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

        private void DropFrame(RenderOutputFrameLease lease)
        {
            DisposeDroppedFrame(lease);
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
