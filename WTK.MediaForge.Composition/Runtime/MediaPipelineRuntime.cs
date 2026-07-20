using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime;

internal sealed class MediaPipelineRuntime : IRenderedOutputFrameConsumer, IAsyncDisposable
{
    private readonly Dictionary<RenderOutputId, EncodedRenderOutputRoute> _encodedRoutes = [];
    private readonly object _gate = new();
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

    public MediaPipelineRuntime(IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _diagnostics = diagnostics;
        Encoding = new RenderedOutputEncodingPipeline(diagnostics);
    }

    public RenderedOutputEncodingPipeline Encoding { get; }

    public int EncodedOutputCount
    {
        get
        {
            lock (_gate)
                return _encodedRoutes.Count;
        }
    }

    public bool IsEncodedOutputRegistered(RenderOutputId outputId)
    {
        lock (_gate)
            return _encodedRoutes.ContainsKey(outputId);
    }

    public ValueTask RegisterEncodedOutputAsync(
        RenderOutputId outputId,
        RenderedOutputEncodeFrameAdapter frameAdapter,
        IHardwareVideoEncoder encoder,
        IGpuFrameExporter frameExporter,
        EncodedPacketSinkContext sinkContext,
        IEnumerable<IEncodedPacketSink> sinks,
        IMediaTransportAuditSink auditSink,
        int exportQueueCapacity = 4,
        int encodeQueueCapacity = 12,
        int sinkQueueCapacity = 64,
        TimeSpan? encodeTimeout = null,
        EncodedOutputBackpressurePolicy? backpressurePolicy = null,
        IAsyncDisposable? routeResources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        var sinkList = sinks.ToArray();
        var policy = backpressurePolicy ?? ResolveBackpressurePolicy(sinkList);
        var registrations = sinkList
            .Select(sink => new EncodedOutputSinkRegistration(outputId, sink, policy))
            .ToArray();
        return RegisterEncodedOutputGroupAsync(
            outputId,
            frameAdapter,
            encoder,
            frameExporter,
            sinkContext,
            registrations,
            auditSink,
            exportQueueCapacity,
            encodeQueueCapacity,
            sinkQueueCapacity,
            encodeTimeout,
            routeResources,
            cancellationToken);
    }

    public async ValueTask RegisterEncodedOutputGroupAsync(
        RenderOutputId surfaceOutputId,
        RenderedOutputEncodeFrameAdapter frameAdapter,
        IHardwareVideoEncoder encoder,
        IGpuFrameExporter frameExporter,
        EncodedPacketSinkContext sinkContext,
        IEnumerable<EncodedOutputSinkRegistration> sinkRegistrations,
        IMediaTransportAuditSink auditSink,
        int exportQueueCapacity = 4,
        int encodeQueueCapacity = 12,
        int sinkQueueCapacity = 64,
        TimeSpan? encodeTimeout = null,
        IAsyncDisposable? routeResources = null,
        CancellationToken cancellationToken = default)
    {
        if (surfaceOutputId.IsEmpty)
            throw new ArgumentException("Surface output id cannot be empty.", nameof(surfaceOutputId));

        ArgumentNullException.ThrowIfNull(frameAdapter);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(frameExporter);
        ArgumentNullException.ThrowIfNull(sinkContext);
        ArgumentNullException.ThrowIfNull(sinkRegistrations);
        ArgumentNullException.ThrowIfNull(auditSink);

        var registrations = sinkRegistrations.ToArray();
        if (registrations.Length == 0)
            throw new ArgumentException("At least one encoded packet sink registration is required.", nameof(sinkRegistrations));
        if (registrations.Any(static registration => registration.OutputId.IsEmpty))
            throw new ArgumentException("Encoded packet sink output ids cannot be empty.", nameof(sinkRegistrations));
        var sinkList = registrations.Select(static registration => registration.Sink).ToArray();
        var logicalOutputIds = registrations.Select(static registration => registration.OutputId).Distinct().ToArray();
        var policy = registrations.Any(static registration => !registration.Policy.AllowFrameDrop)
            ? EncodedOutputBackpressurePolicy.Recording()
            : EncodedOutputBackpressurePolicy.Streaming();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var duplicate = logicalOutputIds.FirstOrDefault(_encodedRoutes.ContainsKey);
            if (!duplicate.IsEmpty)
                throw new InvalidOperationException($"Encoded output route {duplicate} is already registered.");
        }

        var startedSinks = new List<IEncodedPacketSink>(sinkList.Length);
        EncodedOutputRouter? router = null;
        EncodeSchedulerTarget? scheduler = null;
        var workers = new Dictionary<RenderOutputId, List<EncodedPacketConsumerWorker>>();

        try
        {
            foreach (var sink in sinkList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sink.StartAsync(sinkContext, cancellationToken).ConfigureAwait(false);
                startedSinks.Add(sink);
            }

            router = new EncodedOutputRouter(encoder, sinkQueueCapacity, _diagnostics);
            foreach (var registration in registrations)
            {
                if (!workers.TryGetValue(registration.OutputId, out var outputWorkers))
                {
                    outputWorkers = [];
                    workers.Add(registration.OutputId, outputWorkers);
                }

                outputWorkers.Add(router.RegisterConsumer(
                    new EncodedPacketSinkConsumer(registration.Sink),
                    CreateConsumerOptionsForSink(registration.Sink, registration.Policy)));
            }

            scheduler = new EncodeSchedulerTarget(
                encoder,
                frameExporter,
                auditSink,
                router.RoutePacket,
                _diagnostics,
                encodeQueueCapacity,
                policy.EncodeQueuePolicy,
                encodeTimeout,
                surfaceOutputId);

            var route = new EncodedRenderOutputRoute(
                surfaceOutputId,
                router,
                registrations,
                workers,
                policy,
                routeResources);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var duplicate = logicalOutputIds.FirstOrDefault(_encodedRoutes.ContainsKey);
                if (!duplicate.IsEmpty)
                    throw new InvalidOperationException($"Encoded output route {duplicate} is already registered.");

                Encoding.RegisterOutput(
                    surfaceOutputId,
                    frameAdapter,
                    scheduler,
                    encoder.InputRequirement,
                    auditSink,
                    exportQueueCapacity,
                    policy);
                foreach (var logicalOutputId in logicalOutputIds)
                    _encodedRoutes.Add(logicalOutputId, route);
            }

            router = null;
            scheduler = null;
            routeResources = null;
        }
        catch (Exception registrationException)
        {
            var errors = new List<Exception> { registrationException };

            if (scheduler is not null)
            {
                try
                {
                    await scheduler.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    errors.Add(cleanupException);
                }
            }

            if (router is not null)
            {
                try
                {
                    await router.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    errors.Add(cleanupException);
                }
            }

            try
            {
                await StopAndDisposeStartedSinksAsync(startedSinks, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                errors.Add(cleanupException);
            }

            if (routeResources is not null)
            {
                try
                {
                    await routeResources.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    errors.Add(cleanupException);
                }
            }

            throw new AggregateException($"Failed to register encoded output group for surface {surfaceOutputId}.", errors);
        }
    }

    public async ValueTask UnregisterEncodedOutputAsync(
        RenderOutputId outputId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        EncodedRenderOutputRoute? route;
        lock (_gate)
        {
            if (!_encodedRoutes.TryGetValue(outputId, out route))
                return;

            foreach (var logicalOutputId in route.OutputIds)
                _encodedRoutes.Remove(logicalOutputId);
        }

        await Encoding.StopOutputAsync(route.SurfaceOutputId, timeout, cancellationToken).ConfigureAwait(false);
        await route.DisposeAsync(cancellationToken).ConfigureAwait(false);
    }

    public void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch) =>
        Encoding.PublishCompletedFrames(frameBatch);

    public IReadOnlyList<EncodedOutputRuntimeSnapshot> GetEncodedOutputRuntimeSnapshots()
    {
        KeyValuePair<RenderOutputId, EncodedRenderOutputRoute>[] routes;
        lock (_gate)
            routes = _encodedRoutes.ToArray();

        var snapshots = new List<EncodedOutputRuntimeSnapshot>(routes.Length);
        foreach (var pair in routes)
        {
            var logicalOutputId = pair.Key;
            var route = pair.Value;
            if (!Encoding.TryGetSnapshot(route.SurfaceOutputId, out var snapshot))
            {
                snapshots.Add(new EncodedOutputRuntimeSnapshot(
                    logicalOutputId,
                    EncodedOutputRuntimeStatus.Stopped,
                    null,
                    0,
                    0,
                    0,
                    0,
                    TimeSpan.Zero));
                continue;
            }

            var stats = route.GetConsumerStatistics(logicalOutputId);
            var packetsWritten = stats.WrittenPackets;
            var droppedPackets = stats.DroppedPackets;
            var failed = stats.FailedWrites > 0 ||
                         stats.TimedOutWrites > 0 ||
                         !string.IsNullOrWhiteSpace(stats.LastError);

            var status = snapshot.Status;
            var reason = snapshot.Reason;
            if (failed)
            {
                status = EncodedOutputRuntimeStatus.Failed;
                reason = stats.LastError ??
                         $"Encoded sink '{stats.DisplayName}' failed or timed out while writing packets.";
            }
            else if (droppedPackets > 0 && snapshot.Status != EncodedOutputRuntimeStatus.Failed)
            {
                status = EncodedOutputRuntimeStatus.Backpressure;
                reason ??= "One or more encoded packet consumers dropped packets because their queue was full.";
            }

            snapshots.Add(snapshot with
            {
                OutputId = logicalOutputId,
                Status = status,
                Reason = reason,
                PacketsWritten = packetsWritten,
                FramesDropped = snapshot.FramesDropped + droppedPackets
            });
        }

        return snapshots;
    }

    public async ValueTask DisposeAsync()
    {
        EncodedRenderOutputRoute[] routes;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            routes = _encodedRoutes.Values.Distinct().ToArray();
            _encodedRoutes.Clear();
        }

        List<Exception>? errors = null;
        try
        {
            await Encoding.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        foreach (var route in routes)
        {
            try
            {
                await route.DisposeAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to dispose media pipeline runtime.", errors);
    }

    private static async ValueTask StopAndDisposeStartedSinksAsync(
        IReadOnlyList<IEncodedPacketSink> sinks,
        CancellationToken cancellationToken)
    {
        List<Exception>? errors = null;
        foreach (var sink in sinks)
        {
            try
            {
                await sink.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await sink.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
            throw new AggregateException("Failed to stop one or more encoded packet sinks.", errors);
    }

    private static EncodedOutputBackpressurePolicy ResolveBackpressurePolicy(IReadOnlyList<IEncodedPacketSink> sinks)
    {
        if (sinks.Any(static sink => sink is RecordingMp4Sink or RecordingMp4PacketSink))
            return EncodedOutputBackpressurePolicy.Recording();

        if (sinks.Any(static sink => sink is RtmpSink or RtmpPacketSink))
            return EncodedOutputBackpressurePolicy.Streaming();

        return EncodedOutputBackpressurePolicy.Diagnostics();
    }

    private static EncodedPacketConsumerOptions CreateConsumerOptionsForSink(
        IEncodedPacketSink sink,
        EncodedOutputBackpressurePolicy policy) =>
        sink switch
        {
            RecordingMp4Sink or RecordingMp4PacketSink => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = policy.SinkPolicy,
                RequiresLosslessDelivery = true,
                WriteTimeout = policy.SinkWriteTimeout,
                DisplayName = sink.GetType().Name
            },
            RtmpSink or RtmpPacketSink => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = policy.SinkPolicy,
                RequiresLosslessDelivery = false,
                WriteTimeout = policy.SinkWriteTimeout,
                DisplayName = sink.GetType().Name
            },
            _ => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = policy.SinkPolicy,
                WriteTimeout = policy.SinkWriteTimeout,
                DisplayName = sink.GetType().Name
            }
        };

    private sealed class EncodedRenderOutputRoute
    {
        private readonly IReadOnlyList<EncodedOutputSinkRegistration> _registrations;
        private readonly IReadOnlyDictionary<RenderOutputId, List<EncodedPacketConsumerWorker>> _workers;
        private readonly IAsyncDisposable? _resources;

        public EncodedRenderOutputRoute(
            RenderOutputId surfaceOutputId,
            EncodedOutputRouter router,
            IReadOnlyList<EncodedOutputSinkRegistration> registrations,
            IReadOnlyDictionary<RenderOutputId, List<EncodedPacketConsumerWorker>> workers,
            EncodedOutputBackpressurePolicy policy,
            IAsyncDisposable? resources)
        {
            SurfaceOutputId = surfaceOutputId;
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            _workers = workers ?? throw new ArgumentNullException(nameof(workers));
            _resources = resources;
        }

        public RenderOutputId SurfaceOutputId { get; }

        public IReadOnlyCollection<RenderOutputId> OutputIds => _workers.Keys.ToArray();

        public EncodedOutputRouter Router { get; }

        public EncodedOutputBackpressurePolicy Policy { get; }

        public EncodedPacketConsumerStatistics GetConsumerStatistics(RenderOutputId outputId)
        {
            if (!_workers.TryGetValue(outputId, out var workers))
                throw new KeyNotFoundException($"Encoded output {outputId} is not part of this route group.");

            var statistics = workers.Select(static worker => worker.GetStatistics()).ToArray();
            return new EncodedPacketConsumerStatistics(
                string.Join(", ", statistics.Select(static item => item.DisplayName)),
                statistics[0].BackpressurePolicy,
                statistics.Sum(static item => item.EnqueuedPackets),
                statistics.Sum(static item => item.WrittenPackets),
                statistics.Sum(static item => item.DroppedPackets),
                statistics.Sum(static item => item.FailedWrites),
                statistics.Sum(static item => item.TimedOutWrites),
                statistics.Select(static item => item.LastError).FirstOrDefault(static error => !string.IsNullOrWhiteSpace(error)));
        }

        public async ValueTask DisposeAsync(CancellationToken cancellationToken)
        {
            List<Exception>? errors = null;

            try
            {
                await Router.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            foreach (var registration in _registrations)
            {
                try
                {
                    await registration.Sink.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            try
            {
                await Router.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            foreach (var registration in _registrations)
            {
                try
                {
                    await registration.Sink.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            if (_resources is not null)
            {
                try
                {
                    await _resources.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            if (errors is not null)
                throw new AggregateException($"Failed to dispose encoded output route group for {SurfaceOutputId}.", errors);
        }
    }
}
