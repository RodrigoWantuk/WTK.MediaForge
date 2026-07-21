using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class EncodeSchedulerTargetTests
{
    [Fact]
    public async Task Encode_scheduler_uses_media_presentation_time()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var packets = new List<EncodedVideoPacket>();
        var encoder = new RecordingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            packets.Add,
            encodeTimeout: TimeSpan.FromSeconds(1));

        target.OnRenderedFrame(CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33)));
        target.OnRenderedFrame(CreateRenderedFrame(pool, 2, TimeSpan.FromMilliseconds(66)));

        await WaitForConditionAsync(() => packets.Count == 2, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(33), packets[0].PresentationTime);
        Assert.Equal(TimeSpan.FromMilliseconds(66), packets[1].PresentationTime);
        Assert.All(encoder.Contexts, context =>
            Assert.True(context.PresentationTime < TimeSpan.FromDays(1)));
    }

    [Fact]
    public async Task Encode_scheduler_pairs_each_packet_with_its_rendered_texture()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var packets = new List<EncodedVideoPacket>();
        var encoder = new RecordingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            packets.Add,
            queueCapacity: 3,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.QueueWithBackpressure,
            encodeTimeout: TimeSpan.FromSeconds(1));

        var frameA = CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33));
        var frameB = CreateRenderedFrame(pool, 2, TimeSpan.FromMilliseconds(66));
        var frameC = CreateRenderedFrame(pool, 3, TimeSpan.FromMilliseconds(99));

        var expected = new[]
        {
            new EncodedFrameRecord(1, TimeSpan.FromMilliseconds(33), frameA.TextureLease!.TextureId),
            new EncodedFrameRecord(2, TimeSpan.FromMilliseconds(66), frameB.TextureLease!.TextureId),
            new EncodedFrameRecord(3, TimeSpan.FromMilliseconds(99), frameC.TextureLease!.TextureId)
        };

        target.OnRenderedFrame(frameA);
        target.OnRenderedFrame(frameB);
        target.OnRenderedFrame(frameC);

        await WaitForConditionAsync(() => encoder.EncodedFrames.Count == 3, TimeSpan.FromSeconds(2));

        Assert.Equal(expected, encoder.EncodedFrames);
    }

    [Fact]
    public async Task Encode_scheduler_timeout_cancels_encoder_call()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var diagnostics = new ListDiagnosticsSink();
        var encoder = new BlockingEncoder();
        var outputId = RenderOutputId.New();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            diagnostics,
            encodeTimeout: TimeSpan.FromMilliseconds(50),
            outputId: outputId);

        target.OnRenderedFrame(CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33)));

        await WaitForConditionAsync(
            () => encoder.CancellationObserved &&
                  diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_timeout"),
            TimeSpan.FromSeconds(2));

        var diagnostic = Assert.Single(
            diagnostics.Diagnostics,
            item => item.Code == "engine.encode_scheduler_frame_timeout");
        Assert.Equal(outputId.Value, diagnostic.OutputId);
    }

    [Fact]
    public async Task QueueWithBackpressure_enforces_capacity_and_reports_dropped_frame()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var diagnostics = new ListDiagnosticsSink();
        var encoder = new BlockingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            diagnostics,
            queueCapacity: 1,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.QueueWithBackpressure,
            encodeTimeout: TimeSpan.FromSeconds(5));

        target.OnRenderedFrame(CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33)));
        await WaitForConditionAsync(() => encoder.Started, TimeSpan.FromSeconds(2));

        var second = CreateRenderedFrame(pool, 2, TimeSpan.FromMilliseconds(66));
        var third = CreateRenderedFrame(pool, 3, TimeSpan.FromMilliseconds(99));
        var queuedLease = second.TextureLease!;
        var rejectedLease = third.TextureLease!;

        target.OnRenderedFrame(second);
        target.OnRenderedFrame(third);

        await WaitForConditionAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_dropped_backpressure") &&
                  target.Status == EncodedOutputRuntimeStatus.Failed &&
                  queuedLease.TextureId == default &&
                  rejectedLease.TextureId == default,
            TimeSpan.FromSeconds(2));

        Assert.Equal(0, target.PendingFrameCount);
        Assert.Equal(default, queuedLease.TextureId);
        Assert.Equal(default, rejectedLease.TextureId);
        Assert.Equal(EncodedOutputRuntimeStatus.Failed, target.Status);
        Assert.Contains("does not allow frame drops", target.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, target.FramesDropped);

        var afterFailure = CreateRenderedFrame(pool, 4, TimeSpan.FromMilliseconds(132));
        var afterFailureLease = afterFailure.TextureLease!;
        target.OnRenderedFrame(afterFailure);
        Assert.Equal(default, afterFailureLease.TextureId);
    }

    [Fact]
    public async Task KeepLatest_enforces_capacity_and_keeps_newest_pending_frame()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var diagnostics = new ListDiagnosticsSink();
        var encoder = new BlockingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            diagnostics,
            queueCapacity: 1,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.KeepLatest,
            encodeTimeout: TimeSpan.FromSeconds(5));

        target.OnRenderedFrame(CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33)));
        await WaitForConditionAsync(() => encoder.Started, TimeSpan.FromSeconds(2));

        var second = CreateRenderedFrame(pool, 2, TimeSpan.FromMilliseconds(66));
        var third = CreateRenderedFrame(pool, 3, TimeSpan.FromMilliseconds(99));
        var replacedLease = second.TextureLease!;

        target.OnRenderedFrame(second);
        target.OnRenderedFrame(third);

        await WaitForConditionAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_dropped_backpressure"),
            TimeSpan.FromSeconds(2));

        Assert.Equal(1, target.PendingFrameCount);
        Assert.Equal(default, replacedLease.TextureId);

        var stopFailure = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await target.StopAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));
        Assert.Contains("forced cancellation completed", stopFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(encoder.CancellationObserved);
    }

    [Fact]
    public async Task Stop_releases_processing_and_pending_rendered_frame_leases()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var encoder = new RecordingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            queueCapacity: 3,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.QueueWithBackpressure,
            encodeTimeout: TimeSpan.FromSeconds(1));

        var first = CreateRenderedFrame(pool, 1, TimeSpan.FromMilliseconds(33));
        var second = CreateRenderedFrame(pool, 2, TimeSpan.FromMilliseconds(66));
        var third = CreateRenderedFrame(pool, 3, TimeSpan.FromMilliseconds(99));
        var firstLease = first.TextureLease!;
        var secondLease = second.TextureLease!;
        var thirdLease = third.TextureLease!;

        target.OnRenderedFrame(first);
        target.OnRenderedFrame(second);
        target.OnRenderedFrame(third);

        await target.StopAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(default, firstLease.TextureId);
        Assert.Equal(default, secondLease.TextureId);
        Assert.Equal(default, thirdLease.TextureId);
    }

    [Fact]
    public async Task Encode_scheduler_uses_pre_exported_encoder_input_without_frame_exporter()
    {
        var packets = new List<EncodedVideoPacket>();
        var encoder = new PreExportedInputRecordingEncoder();
        var audit = new CollectingMediaTransportAuditSink();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            audit,
            packets.Add,
            encodeTimeout: TimeSpan.FromSeconds(1));

        using var inputLease = HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
        {
            Width = 320,
            Height = 180,
            Format = "B8G8R8A8_UNORM",
            TransportKind = MediaTransportKind.GpuSurface
        });

        target.OnRenderedFrame(new ScheduledRenderedFrame
        {
            Context = CreateContext(1, TimeSpan.FromMilliseconds(33)),
            EncoderInputLease = inputLease
        });

        await WaitForConditionAsync(() => packets.Count == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(1, encoder.EncodeAsyncCalls);
        Assert.Equal(0, encoder.SubmitFrameAsyncCalls);
        Assert.Equal(TimeSpan.FromMilliseconds(33), packets[0].PresentationTime);
    }

    [Fact]
    public async Task Stop_routes_packets_produced_during_encoder_drain()
    {
        var drainedPacket = new EncodedVideoPacket
        {
            Data = new byte[] { 7, 8, 9 },
            Codec = EncodedVideoCodec.H264,
            PresentationTime = TimeSpan.FromMilliseconds(99),
            Duration = TimeSpan.FromMilliseconds(33),
            IsKeyFrame = false
        };
        var packets = new List<EncodedVideoPacket>();
        var encoder = new RecordingEncoder([drainedPacket]);
        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            packets.Add);

        await target.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal([drainedPacket], packets);
        Assert.Equal(1, target.PacketsProduced);
    }

    [Fact]
    public async Task Stop_propagates_encoder_drain_failure_and_marks_route_failed()
    {
        var diagnostics = new ListDiagnosticsSink();
        var encoder = new RecordingEncoder(drainFailure: new InvalidOperationException("Drain failed."));
        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            diagnostics);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await target.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));

        Assert.Equal("Drain failed.", exception.Message);
        Assert.Equal(EncodedOutputRuntimeStatus.Failed, target.Status);
        Assert.Contains("finalization failed", target.StatusReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Code == "engine.encode_scheduler_drain_failed" &&
            diagnostic.Exception is InvalidOperationException);
    }

    private static FrameExecutionContext CreateContext(long frameId, TimeSpan presentationTime) =>
        new()
        {
            FrameId = frameId,
            PresentationTime = presentationTime,
            FrameBudget = TimeSpan.FromMilliseconds(33),
            TargetOutputs = []
        };

    private static ScheduledRenderedFrame CreateRenderedFrame(
        GpuResourcePool pool,
        long frameId,
        TimeSpan presentationTime) =>
        new()
        {
            Context = CreateContext(frameId, presentationTime),
            TextureLease = CreateLease(pool)
        };

    private static GpuTextureLease CreateLease(GpuResourcePool pool) =>
        pool.AcquireTexture(new GpuTextureDescriptor
        {
            Width = 320,
            Height = 180,
            Format = "B8G8R8A8_UNORM",
            Usage = GpuTextureUsage.OffscreenColor
        });

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        if (condition())
            return;

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class RecordingEncoder : IHardwareVideoEncoder
    {
        private readonly IReadOnlyList<EncodedVideoPacket> _drainedPackets;
        private readonly Exception? _drainFailure;

        public RecordingEncoder(
            IReadOnlyList<EncodedVideoPacket>? drainedPackets = null,
            Exception? drainFailure = null)
        {
            _drainedPackets = drainedPackets ?? [];
            _drainFailure = drainFailure;
        }

        public List<HardwareEncodeFrameContext> Contexts { get; } = [];

        public List<EncodedFrameRecord> EncodedFrames { get; } = [];

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Fake",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test",
            AcceptsGpuSurfaceInput = true
        };

        public HardwareEncoderInputRequirement InputRequirement { get; } = new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = "B8G8R8A8_UNORM",
            RequiresGpuSurface = true
        };

        public ValueTask<EncodedVideoPacket?> EncodeAsync(
            EncodeFrameContext context,
            IMediaTransportAuditSink auditSink) =>
            ValueTask.FromResult<EncodedVideoPacket?>(null);

        public ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
            GpuTextureLease textureLease,
            HardwareEncodeFrameContext context,
            IGpuFrameExporter frameExporter,
            IMediaTransportAuditSink auditSink)
        {
            Contexts.Add(context);
            EncodedFrames.Add(new EncodedFrameRecord(
                context.FrameId,
                context.PresentationTime,
                textureLease.TextureId));
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
            {
                Data = new byte[] { 1, 2, 3 },
                Codec = EncodedVideoCodec.H264,
                PresentationTime = context.PresentationTime,
                IsKeyFrame = context.FrameId == 1
            });
        }

        public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_drainFailure is not null)
                return ValueTask.FromException<IReadOnlyList<EncodedVideoPacket>>(_drainFailure);

            return ValueTask.FromResult(_drainedPackets);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private readonly record struct EncodedFrameRecord(
        long FrameId,
        TimeSpan PresentationTime,
        GpuTextureId TextureId);

    private sealed class BlockingEncoder : IHardwareVideoEncoder
    {
        public bool Started { get; private set; }

        public bool CancellationObserved { get; private set; }

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Blocking",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test",
            AcceptsGpuSurfaceInput = true
        };

        public HardwareEncoderInputRequirement InputRequirement { get; } = new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = "B8G8R8A8_UNORM",
            RequiresGpuSurface = true
        };

        public ValueTask<EncodedVideoPacket?> EncodeAsync(
            EncodeFrameContext context,
            IMediaTransportAuditSink auditSink) =>
            ValueTask.FromResult<EncodedVideoPacket?>(null);

        public async ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
            GpuTextureLease textureLease,
            HardwareEncodeFrameContext context,
            IGpuFrameExporter frameExporter,
            IMediaTransportAuditSink auditSink)
        {
            Started = true;

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return null;
        }

        public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGpuFrameExporter : IGpuFrameExporter
    {
        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
            true;

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(HardwareEncoderInputLease.Create(descriptor));
    }

    private sealed class ThrowingGpuFrameExporter : IGpuFrameExporter
    {
        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
            throw new InvalidOperationException("Pre-exported encoder input should not use the frame exporter.");

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pre-exported encoder input should not use the frame exporter.");
    }

    private sealed class PreExportedInputRecordingEncoder : IHardwareVideoEncoder
    {
        public int EncodeAsyncCalls { get; private set; }

        public int SubmitFrameAsyncCalls { get; private set; }

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "PreExported",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test",
            AcceptsGpuSurfaceInput = true
        };

        public HardwareEncoderInputRequirement InputRequirement { get; } = new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = "B8G8R8A8_UNORM",
            RequiresGpuSurface = true
        };

        public ValueTask<EncodedVideoPacket?> EncodeAsync(
            EncodeFrameContext context,
            IMediaTransportAuditSink auditSink)
        {
            EncodeAsyncCalls++;
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
            {
                Data = new byte[] { 4, 5, 6 },
                Codec = EncodedVideoCodec.H264,
                PresentationTime = context.PresentationTime,
                IsKeyFrame = context.FrameNumber == 1
            });
        }

        public ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
            GpuTextureLease textureLease,
            HardwareEncodeFrameContext context,
            IGpuFrameExporter frameExporter,
            IMediaTransportAuditSink auditSink)
        {
            SubmitFrameAsyncCalls++;
            return ValueTask.FromResult<EncodedVideoPacket?>(null);
        }

        public ValueTask<IReadOnlyList<EncodedVideoPacket>> DrainAsync(
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<EncodedVideoPacket>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGpuTextureFactory : IGpuTextureFactory
    {
        public IGpuPhysicalResource CreateTexture(GpuTextureDescriptor descriptor) =>
            new FakePhysicalResource();
    }

    private sealed class FakePhysicalResource : IGpuPhysicalResource
    {
        public Task FullyDisposed => Task.CompletedTask;

        public bool TryFinalizePhysicalResources() => true;
    }
}
