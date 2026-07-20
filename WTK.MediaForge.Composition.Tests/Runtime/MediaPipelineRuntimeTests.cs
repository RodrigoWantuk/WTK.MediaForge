using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Encode;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.MediaPipeline;

public sealed class MediaPipelineRuntimeTests
{
    [Fact]
    public async Task Encoded_route_encodes_once_and_fans_out_packet_to_multiple_sinks()
    {
        var outputId = RenderOutputId.New();
        var encoder = new PreExportedInputRecordingEncoder();
        var sinkA = new RecordingEncodedPacketSink();
        var sinkB = new RecordingEncodedPacketSink();
        await using var runtime = new MediaPipelineRuntime();

        await runtime.RegisterEncodedOutputAsync(
            outputId,
            new RenderedOutputEncodeFrameAdapter(new ImmediateRenderedOutputExporter()),
            encoder,
            new ThrowingGpuFrameExporter(),
            CreateSinkContext(),
            [sinkA, sinkB],
            new CollectingMediaTransportAuditSink(),
            encodeTimeout: TimeSpan.FromSeconds(1),
            cancellationToken: CancellationToken.None);

        var surface = new TrackingRenderedOutputSurfaceLease(outputId);
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
            [surface],
            new RenderFrameContext(23, TimeSpan.FromMilliseconds(759), TimeSpan.FromMilliseconds(33), 30, CancellationToken.None));

        runtime.PublishCompletedFrames(batch);

        await WaitForConditionAsync(
            () => sinkA.Packets.Count == 1 && sinkB.Packets.Count == 1,
            TimeSpan.FromSeconds(2));
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(1, encoder.EncodeAsyncCalls);
        Assert.Equal(TimeSpan.FromMilliseconds(759), sinkA.Packets[0].PresentationTime);
        Assert.Same(sinkA.Packets[0], sinkB.Packets[0]);
        Assert.Equal(1, surface.DisposeCount);

        var snapshot = Assert.Single(runtime.GetEncodedOutputRuntimeSnapshots());
        Assert.Equal(outputId, snapshot.OutputId);
        Assert.Equal(EncodedOutputRuntimeStatus.Running, snapshot.Status);
        Assert.Equal(1, snapshot.PacketsProduced);
        Assert.Equal(2, snapshot.PacketsWritten);
        Assert.Equal(0, snapshot.FramesDropped);
    }

    [Fact]
    public async Task Register_encoded_route_rolls_back_started_sinks_when_later_sink_fails()
    {
        var outputId = RenderOutputId.New();
        var startedSink = new RecordingEncodedPacketSink();
        var failingSink = new FailingStartEncodedPacketSink();
        await using var runtime = new MediaPipelineRuntime();

        await Assert.ThrowsAsync<AggregateException>(async () =>
            await runtime.RegisterEncodedOutputAsync(
                outputId,
                new RenderedOutputEncodeFrameAdapter(new ImmediateRenderedOutputExporter()),
                new PreExportedInputRecordingEncoder(),
                new ThrowingGpuFrameExporter(),
                CreateSinkContext(),
                [startedSink, failingSink],
                new CollectingMediaTransportAuditSink(),
                cancellationToken: CancellationToken.None));

        Assert.Equal(0, runtime.EncodedOutputCount);
        Assert.Equal(1, startedSink.StartCount);
        Assert.Equal(1, startedSink.StopCount);
        Assert.Equal(1, startedSink.DisposeCount);
        Assert.Equal(1, failingSink.StartCount);
    }

    [Fact]
    public async Task Unregister_encoded_route_stops_scheduler_router_encoder_and_sinks()
    {
        var outputId = RenderOutputId.New();
        var encoder = new PreExportedInputRecordingEncoder();
        var sink = new RecordingEncodedPacketSink();
        await using var runtime = new MediaPipelineRuntime();

        await runtime.RegisterEncodedOutputAsync(
            outputId,
            new RenderedOutputEncodeFrameAdapter(new ImmediateRenderedOutputExporter()),
            encoder,
            new ThrowingGpuFrameExporter(),
            CreateSinkContext(),
            [sink],
            new CollectingMediaTransportAuditSink(),
            cancellationToken: CancellationToken.None);

        await runtime.UnregisterEncodedOutputAsync(outputId, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(0, runtime.EncodedOutputCount);
        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
        Assert.Equal(1, encoder.DisposeCount);
    }

    [Fact]
    public async Task Compatible_logical_outputs_share_one_surface_and_encoder()
    {
        var surfaceOutputId = RenderOutputId.New();
        var aliasOutputId = RenderOutputId.New();
        var encoder = new PreExportedInputRecordingEncoder();
        var recording = new RecordingEncodedPacketSink();
        var streaming = new RecordingEncodedPacketSink();
        await using var runtime = new MediaPipelineRuntime();

        await runtime.RegisterEncodedOutputGroupAsync(
            surfaceOutputId,
            new RenderedOutputEncodeFrameAdapter(new ImmediateRenderedOutputExporter()),
            encoder,
            new ThrowingGpuFrameExporter(),
            CreateSinkContext(),
            [
                new EncodedOutputSinkRegistration(
                    surfaceOutputId,
                    recording,
                    EncodedOutputBackpressurePolicy.Recording()),
                new EncodedOutputSinkRegistration(
                    aliasOutputId,
                    streaming,
                    EncodedOutputBackpressurePolicy.Streaming())
            ],
            new CollectingMediaTransportAuditSink(),
            cancellationToken: CancellationToken.None);

        var surface = new TrackingRenderedOutputSurfaceLease(surfaceOutputId);
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        runtime.PublishCompletedFrames(batch);

        await WaitForConditionAsync(
            () => recording.Packets.Count == 1 && streaming.Packets.Count == 1,
            TimeSpan.FromSeconds(2));
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(1, encoder.EncodeAsyncCalls);
        Assert.Same(recording.Packets[0], streaming.Packets[0]);
        Assert.Equal(2, runtime.EncodedOutputCount);
        var snapshots = runtime.GetEncodedOutputRuntimeSnapshots();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(1, snapshot.PacketsWritten));

        await runtime.UnregisterEncodedOutputAsync(aliasOutputId, TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(0, runtime.EncodedOutputCount);
        Assert.Equal(1, encoder.DisposeCount);
    }

    [Fact]
    public async Task Failed_encoded_sink_does_not_stop_compatible_output()
    {
        var failedOutputId = RenderOutputId.New();
        var healthyOutputId = RenderOutputId.New();
        var encoder = new PreExportedInputRecordingEncoder();
        var failed = new FailingWriteEncodedPacketSink();
        var healthy = new RecordingEncodedPacketSink();
        var runtime = new MediaPipelineRuntime();

        await runtime.RegisterEncodedOutputGroupAsync(
            failedOutputId,
            new RenderedOutputEncodeFrameAdapter(new ImmediateRenderedOutputExporter()),
            encoder,
            new ThrowingGpuFrameExporter(),
            CreateSinkContext(),
            [
                new EncodedOutputSinkRegistration(
                    failedOutputId,
                    failed,
                    EncodedOutputBackpressurePolicy.Streaming()),
                new EncodedOutputSinkRegistration(
                    healthyOutputId,
                    healthy,
                    EncodedOutputBackpressurePolicy.Streaming())
            ],
            new CollectingMediaTransportAuditSink(),
            cancellationToken: CancellationToken.None);

        for (var frameId = 1; frameId <= 2; frameId++)
        {
            var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
                [new TrackingRenderedOutputSurfaceLease(failedOutputId)],
                new RenderFrameContext(frameId, TimeSpan.FromMilliseconds(frameId * 33), TimeSpan.FromMilliseconds(33), 30, CancellationToken.None));
            runtime.PublishCompletedFrames(batch);
            await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        }

        await WaitForConditionAsync(
            () => healthy.Packets.Count == 2 &&
                  runtime.GetEncodedOutputRuntimeSnapshots().Any(snapshot =>
                      snapshot.OutputId == failedOutputId && snapshot.Status == EncodedOutputRuntimeStatus.Failed),
            TimeSpan.FromSeconds(2));

        Assert.Equal(2, encoder.EncodeAsyncCalls);
        Assert.Equal(EncodedOutputRuntimeStatus.Running,
            runtime.GetEncodedOutputRuntimeSnapshots().Single(snapshot => snapshot.OutputId == healthyOutputId).Status);

        var cleanup = await Assert.ThrowsAsync<AggregateException>(async () => await runtime.DisposeAsync());
        Assert.Contains("Synthetic sink failure", cleanup.ToString(), StringComparison.Ordinal);
    }

    private static EncodedPacketSinkContext CreateSinkContext() =>
        new()
        {
            Codec = EncodedVideoCodec.H264,
            Size = new FrameSize(320, 180),
            FramesPerSecond = 30
        };

    private static HardwareEncoderInputRequirement CreateRequirement() =>
        new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = "B8G8R8A8_UNORM",
            RequiresGpuSurface = true
        };

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met before timeout.");
    }

    private sealed class RecordingEncodedPacketSink : IEncodedPacketSink
    {
        private readonly object _gate = new();
        private readonly List<EncodedVideoPacket> _packets = [];

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IReadOnlyList<EncodedVideoPacket> Packets
        {
            get
            {
                lock (_gate)
                    return _packets.ToArray();
            }
        }

        public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
        {
            lock (_gate)
                _packets.Add(packet);

            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingStartEncodedPacketSink : IEncodedPacketSink
    {
        public int StartCount { get; private set; }

        public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
        {
            StartCount++;
            throw new InvalidOperationException("Sink start failed.");
        }

        public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sink was not started.");

        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingWriteEncodedPacketSink : IEncodedPacketSink
    {
        public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Synthetic sink failure."));

        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ImmediateRenderedOutputExporter : IRenderedOutputEncoderSurfaceExporter
    {
        public bool CanExport(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement) =>
            surface.BackendSurface is not null;

        public ValueTask<HardwareEncoderInputLease> ExportAsync(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
            {
                Width = checked((int)surface.Size.Width),
                Height = checked((int)surface.Size.Height),
                Format = requirement.PixelFormat,
                TransportKind = MediaTransportKind.GpuSurface
            }));
    }

    private sealed class TrackingRenderedOutputSurfaceLease(RenderOutputId outputId) : IRenderedOutputSurfaceLease
    {
        private int _disposeCount;

        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = new(320, 180);

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object BackendSurface { get; } = new();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PreExportedInputRecordingEncoder : IHardwareVideoEncoder
    {
        private int _encodeAsyncCalls;
        private int _disposeCount;

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Test encoder",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test"
        };

        public HardwareEncoderInputRequirement InputRequirement => CreateRequirement();

        public int EncodeAsyncCalls => Volatile.Read(ref _encodeAsyncCalls);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask<EncodedVideoPacket?> EncodeAsync(
            EncodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            Interlocked.Increment(ref _encodeAsyncCalls);
            return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
            {
                Codec = EncodedVideoCodec.H264,
                BitstreamFormat = EncodedVideoBitstreamFormat.AnnexB,
                Data = new byte[] { 0, 0, 0, 1, 0x65 },
                PresentationTime = context.PresentationTime,
                Duration = TimeSpan.FromMilliseconds(33),
                IsKeyFrame = true
            });
        }

        public ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
            GpuTextureLease textureLease,
            HardwareEncodeFrameContext context,
            IGpuFrameExporter frameExporter,
            IMediaTransportAuditSink auditSink) =>
            throw new InvalidOperationException("Runtime should pass a pre-exported encoder input lease.");

        public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>([]);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingGpuFrameExporter : IGpuFrameExporter
    {
        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
            throw new InvalidOperationException("Rendered output runtime should export surfaces before scheduling.");

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rendered output runtime should export surfaces before scheduling.");
    }
}
