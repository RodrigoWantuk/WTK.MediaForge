using System.Diagnostics;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Snapshots;
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
        var accepted = false;

        try
        {
            await sink.StartAsync(context, cancellationToken).ConfigureAwait(false);

            registration = new SinkRegistration(
                output.Id,
                sink,
                DefaultQueueCapacity,
                _diagnostics);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                var registrations = GetOrCreateRegistrationsLocked(output.Id);
                if (registrations.Any(existing => existing.Sink.Id == sink.Id))
                {
                    throw new InvalidOperationException(
                        $"Render output sink {sink.Id} is already attached to output {output.Id}.");
                }

                registrations.Add(registration);
                accepted = true;
            }

            registration.Start();
        }
        finally
        {
            if (!accepted)
            {
                await StopAndDisposeSinkAsync(sink, cancellationToken).ConfigureAwait(false);
                if (registration is not null)
                    await registration.DisposeAsync().ConfigureAwait(false);
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

    public void PublishFrame(RenderFrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        List<(SinkRegistration Registration, RenderOutputFrameLease Lease)> deliveries = [];

        lock (_gate)
        {
            if (_disposed)
                return;

            foreach (var output in snapshot.Outputs)
            {
                if (!_registrations.TryGetValue(output.Id, out var registrations) ||
                    registrations.Count == 0)
                {
                    continue;
                }

                var frameNumber = NextFrameNumberLocked(output.Id);
                var timestamp = _clock.Elapsed;

                foreach (var registration in registrations)
                {
                    var info = new RenderOutputFrameInfo(
                        output.Id,
                        registration.Sink.Id,
                        frameNumber,
                        timestamp,
                        output.OutputSize,
                        RenderPixelFormat.Rgba8Unorm,
                        RenderBackendKind.Vulkan);

                    deliveries.Add((registration, new RenderOutputFrameLease(info)));
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
        private readonly object _gate = new();
        private readonly Queue<RenderOutputFrameLease> _queue = [];
        private readonly SemaphoreSlim _available = new(0);
        private readonly CancellationTokenSource _stop = new();
        private readonly int _capacity;
        private readonly IMediaForgeDiagnosticsSink? _diagnostics;
        private Task? _worker;
        private bool _accepting = true;
        private int _disposed;

        public SinkRegistration(
            RenderOutputId outputId,
            PublicRenderOutputSink sink,
            int capacity,
            IMediaForgeDiagnosticsSink? diagnostics)
        {
            OutputId = outputId;
            Sink = sink;
            _capacity = Math.Max(1, capacity);
            _diagnostics = diagnostics;
        }

        public RenderOutputId OutputId { get; }

        public PublicRenderOutputSink Sink { get; }

        public void Start() =>
            _worker = Task.Run(ProcessAsync);

        public void TryEnqueue(RenderOutputFrameLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            RenderOutputFrameLease? dropped = null;
            var enqueued = false;

            lock (_gate)
            {
                if (!_accepting)
                {
                    dropped = lease;
                }
                else if (_queue.Count < _capacity)
                {
                    _queue.Enqueue(lease);
                    enqueued = true;
                }
                else
                {
                    switch (Sink.BackpressureMode)
                    {
                        case RenderOutputSinkBackpressureMode.DropNewest:
                        case RenderOutputSinkBackpressureMode.BlockProducer:
                            dropped = lease;
                            break;
                        case RenderOutputSinkBackpressureMode.DropOldest:
                        case RenderOutputSinkBackpressureMode.KeepLatest:
                            dropped = _queue.Dequeue();
                            _queue.Enqueue(lease);
                            enqueued = true;
                            break;
                        default:
                            dropped = lease;
                            break;
                    }
                }
            }

            if (dropped is not null)
            {
                _ = DisposeDroppedFrameAsync(dropped);
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Warning,
                    "sink.frame_dropped_backpressure",
                    $"Frame for output {OutputId} was dropped because sink {Sink.Id} is backpressured.",
                    nameof(RenderOutputSinkDispatcher));
            }

            if (enqueued)
                _available.Release();
        }

        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            lock (_gate)
                _accepting = false;

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

                lock (_gate)
                {
                    if (_queue.Count > 0)
                        lease = _queue.Dequeue();
                    else if (!_accepting)
                        return;
                }

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
            List<RenderOutputFrameLease> leases = [];

            lock (_gate)
            {
                while (_queue.Count > 0)
                    leases.Add(_queue.Dequeue());
            }

            foreach (var lease in leases)
                await DisposeDroppedFrameAsync(lease).ConfigureAwait(false);
        }

        private static async ValueTask DisposeDroppedFrameAsync(RenderOutputFrameLease lease) =>
            await lease.DisposeAsync().ConfigureAwait(false);
    }
}
