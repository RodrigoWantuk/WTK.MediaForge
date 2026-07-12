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
                    CreateConsumerOptionsForSink(sink));
            }

            scheduler = new EncodeSchedulerTarget(
                encoder,
                frameExporter,
                auditSink,
                router.RoutePacket,
                _diagnostics,
                encodeQueueCapacity,
                EncodeSchedulerBackpressurePolicy.KeepLatest,
                encodeTimeout);

            var route = new EncodedRenderOutputRoute(outputId, router, startedSinks);
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
                    exportQueueCapacity);
                _encodedRoutes.Add(outputId, route);
            }

            router = null;
            scheduler = null;
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

    private static EncodedPacketConsumerOptions CreateConsumerOptionsForSink(IEncodedPacketSink sink) =>
        sink switch
        {
            RecordingMp4Sink or RecordingMp4PacketSink => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.Backpressure,
                WriteTimeout = TimeSpan.FromSeconds(10),
                DisplayName = sink.GetType().Name
            },
            RtmpSink or RtmpPacketSink => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.FailOutput,
                WriteTimeout = TimeSpan.FromSeconds(5),
                DisplayName = sink.GetType().Name
            },
            _ => new EncodedPacketConsumerOptions
            {
                BackpressurePolicy = EncodedPacketConsumerBackpressurePolicy.FailOutput,
                WriteTimeout = TimeSpan.FromSeconds(5),
                DisplayName = sink.GetType().Name
            }
        };

    private sealed class EncodedRenderOutputRoute(
        RenderOutputId outputId,
        EncodedOutputRouter router,
        IReadOnlyList<IEncodedPacketSink> sinks)
    {
        public RenderOutputId OutputId { get; } = outputId;

        public async ValueTask DisposeAsync(CancellationToken cancellationToken)
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
            }

            try
            {
                await router.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            foreach (var sink in sinks)
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

            if (errors is not null)
                throw new AggregateException($"Failed to dispose encoded output route {OutputId}.", errors);
        }
    }
}
