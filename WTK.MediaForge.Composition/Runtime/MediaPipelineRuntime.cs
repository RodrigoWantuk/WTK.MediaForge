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

    public async ValueTask RegisterEncodedOutputAsync(
        RenderOutputId outputId,
        RenderedOutputEncodeFrameAdapter frameAdapter,
        IHardwareVideoEncoder encoder,
        IGpuFrameExporter frameExporter,
        EncodedPacketSinkContext sinkContext,
        IEnumerable<IEncodedPacketSink> sinks,
        IMediaTransportAuditSink auditSink,
        int exportQueueCapacity = 2,
        int encodeQueueCapacity = 2,
        int sinkQueueCapacity = 8,
        TimeSpan? encodeTimeout = null,
        EncodedOutputBackpressurePolicy? backpressurePolicy = null,
        IAsyncDisposable? routeResources = null,
        CancellationToken cancellationToken = default)
    {
        if (outputId.IsEmpty)
            throw new ArgumentException("Output id cannot be empty.", nameof(outputId));

        ArgumentNullException.ThrowIfNull(frameAdapter);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(frameExporter);
        ArgumentNullException.ThrowIfNull(sinkContext);
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(auditSink);

        var sinkList = sinks.ToArray();
        if (sinkList.Length == 0)
            throw new ArgumentException("At least one encoded packet sink is required.", nameof(sinks));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_encodedRoutes.ContainsKey(outputId))
                throw new InvalidOperationException($"Encoded output route {outputId} is already registered.");
        }

        var startedSinks = new List<IEncodedPacketSink>(sinkList.Length);
        EncodedOutputRouter? router = null;
        EncodeSchedulerTarget? scheduler = null;
        var policy = backpressurePolicy ?? ResolveBackpressurePolicy(sinkList);

        try
        {
            foreach (var sink in sinkList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await sink.StartAsync(sinkContext, cancellationToken).ConfigureAwait(false);
                startedSinks.Add(sink);
            }

            router = new EncodedOutputRouter(encoder, sinkQueueCapacity, _diagnostics);
            foreach (var sink in startedSinks)
            {
                router.RegisterConsumer(
                    new EncodedPacketSinkConsumer(sink),
                    CreateConsumerOptionsForSink(sink, policy));
            }

            scheduler = new EncodeSchedulerTarget(
                encoder,
                frameExporter,
                auditSink,
                router.RoutePacket,
                _diagnostics,
                encodeQueueCapacity,
                policy.EncodeQueuePolicy,
                encodeTimeout);

            var route = new EncodedRenderOutputRoute(outputId, router, startedSinks, policy, routeResources);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_encodedRoutes.ContainsKey(outputId))
                    throw new InvalidOperationException($"Encoded output route {outputId} is already registered.");

                Encoding.RegisterOutput(
                    outputId,
                    frameAdapter,
                    scheduler,
                    encoder.InputRequirement,
                    auditSink,
                    exportQueueCapacity,
                    policy);
                _encodedRoutes.Add(outputId, route);
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

            throw new AggregateException($"Failed to register encoded output route {outputId}.", errors);
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
            if (!_encodedRoutes.Remove(outputId, out route))
                return;
        }

        await Encoding.StopOutputAsync(outputId, timeout, cancellationToken).ConfigureAwait(false);
        await route.DisposeAsync(cancellationToken).ConfigureAwait(false);
    }

    public void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch) =>
        Encoding.PublishCompletedFrames(frameBatch);

    public IReadOnlyList<EncodedOutputRuntimeSnapshot> GetEncodedOutputRuntimeSnapshots()
    {
        EncodedRenderOutputRoute[] routes;
        lock (_gate)
            routes = _encodedRoutes.Values.ToArray();

        var snapshots = new List<EncodedOutputRuntimeSnapshot>(routes.Length);
        foreach (var route in routes)
        {
            if (!Encoding.TryGetSnapshot(route.OutputId, out var snapshot))
            {
                snapshots.Add(new EncodedOutputRuntimeSnapshot(
                    route.OutputId,
                    EncodedOutputRuntimeStatus.Stopped,
                    null,
                    0,
                    0,
                    0,
                    0,
                    TimeSpan.Zero));
                continue;
            }

            var stats = route.Router.GetConsumerStatistics();
            var packetsWritten = stats.Sum(static item => item.WrittenPackets);
            var droppedPackets = stats.Sum(static item => item.DroppedPackets);
            var failed = stats.FirstOrDefault(static item =>
                item.FailedWrites > 0 ||
                item.TimedOutWrites > 0 ||
                !string.IsNullOrWhiteSpace(item.LastError));

            var status = snapshot.Status;
            var reason = snapshot.Reason;
            if (failed is not null)
            {
                status = EncodedOutputRuntimeStatus.Failed;
                reason = failed.LastError ??
                         $"Encoded sink '{failed.DisplayName}' failed or timed out while writing packets.";
            }
            else if (droppedPackets > 0 && snapshot.Status != EncodedOutputRuntimeStatus.Failed)
            {
                status = EncodedOutputRuntimeStatus.Backpressure;
                reason ??= "One or more encoded packet consumers dropped packets because their queue was full.";
            }

            snapshots.Add(snapshot with
            {
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
            routes = _encodedRoutes.Values.ToArray();
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
                IsProductOutput = true,
                WriteTimeout = policy.SinkWriteTimeout,
                DisplayName = sink.GetType().Name
            },
            RtmpSink or RtmpPacketSink => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = policy.SinkPolicy,
                IsProductOutput = true,
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
        private readonly IReadOnlyList<IEncodedPacketSink> _sinks;
        private readonly IAsyncDisposable? _resources;

        public EncodedRenderOutputRoute(
            RenderOutputId outputId,
            EncodedOutputRouter router,
            IReadOnlyList<IEncodedPacketSink> sinks,
            EncodedOutputBackpressurePolicy policy,
            IAsyncDisposable? resources)
        {
            OutputId = outputId;
            Router = router ?? throw new ArgumentNullException(nameof(router));
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
            _resources = resources;
        }

        public RenderOutputId OutputId { get; }

        public EncodedOutputRouter Router { get; }

        public EncodedOutputBackpressurePolicy Policy { get; }

        public async ValueTask DisposeAsync(CancellationToken cancellationToken)
        {
            List<Exception>? errors = null;
            foreach (var sink in _sinks)
            {
                try
                {
                    await sink.StopAsync(cancellationToken).ConfigureAwait(false);
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

            foreach (var sink in _sinks)
            {
                try
                {
                    await sink.DisposeAsync().ConfigureAwait(false);
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
                throw new AggregateException($"Failed to dispose encoded output route {OutputId}.", errors);
        }
    }
}
