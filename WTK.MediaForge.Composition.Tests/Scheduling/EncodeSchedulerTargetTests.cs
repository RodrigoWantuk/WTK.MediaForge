using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Gpu.Resources;
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
            () => CreateLease(pool),
            packets.Add,
            encodeTimeout: TimeSpan.FromSeconds(1));

        target.OnScheduledFrame(CreateContext(1, TimeSpan.FromMilliseconds(33)));
        target.OnScheduledFrame(CreateContext(2, TimeSpan.FromMilliseconds(66)));

        await WaitForConditionAsync(() => packets.Count == 2, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromMilliseconds(33), packets[0].PresentationTime);
        Assert.Equal(TimeSpan.FromMilliseconds(66), packets[1].PresentationTime);
        Assert.All(encoder.Contexts, context =>
            Assert.True(context.PresentationTime < TimeSpan.FromDays(1)));
    }

    [Fact]
    public async Task Encode_scheduler_timeout_cancels_encoder_call()
    {
        using var pool = new GpuResourcePool(new FakeGpuTextureFactory());
        var diagnostics = new ListDiagnosticsSink();
        var encoder = new BlockingEncoder();

        await using var target = new EncodeSchedulerTarget(
            encoder,
            new FakeGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            () => CreateLease(pool),
            _ => { },
            diagnostics,
            encodeTimeout: TimeSpan.FromMilliseconds(50));

        target.OnScheduledFrame(CreateContext(1, TimeSpan.FromMilliseconds(33)));

        await WaitForConditionAsync(
            () => encoder.CancellationObserved &&
                  diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_timeout"),
            TimeSpan.FromSeconds(2));
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
            () => CreateLease(pool),
            _ => { },
            diagnostics,
            queueCapacity: 1,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.QueueWithBackpressure,
            encodeTimeout: TimeSpan.FromSeconds(5));

        target.OnScheduledFrame(CreateContext(1, TimeSpan.FromMilliseconds(33)));
        await WaitForConditionAsync(() => encoder.Started, TimeSpan.FromSeconds(2));

        target.OnScheduledFrame(CreateContext(2, TimeSpan.FromMilliseconds(66)));
        target.OnScheduledFrame(CreateContext(3, TimeSpan.FromMilliseconds(99)));

        await WaitForConditionAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_dropped_backpressure"),
            TimeSpan.FromSeconds(2));

        Assert.True(target.PendingFrameCount <= 1);
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
            () => CreateLease(pool),
            _ => { },
            diagnostics,
            queueCapacity: 1,
            backpressurePolicy: EncodeSchedulerBackpressurePolicy.KeepLatest,
            encodeTimeout: TimeSpan.FromSeconds(5));

        target.OnScheduledFrame(CreateContext(1, TimeSpan.FromMilliseconds(33)));
        await WaitForConditionAsync(() => encoder.Started, TimeSpan.FromSeconds(2));

        target.OnScheduledFrame(CreateContext(2, TimeSpan.FromMilliseconds(66)));
        target.OnScheduledFrame(CreateContext(3, TimeSpan.FromMilliseconds(99)));

        await WaitForConditionAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "engine.encode_scheduler_frame_dropped_backpressure"),
            TimeSpan.FromSeconds(2));

        Assert.Equal(1, target.PendingFrameCount);
    }

    private static FrameExecutionContext CreateContext(long frameId, TimeSpan presentationTime) =>
        new()
        {
            FrameId = frameId,
            PresentationTime = presentationTime,
            FrameBudget = TimeSpan.FromMilliseconds(33),
            TargetOutputs = []
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

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class RecordingEncoder : IHardwareVideoEncoder
    {
        public List<HardwareEncodeFrameContext> Contexts { get; } = [];

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
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<EncodedVideoPacket?>(new EncodedVideoPacket
            {
                Data = new byte[] { 1, 2, 3 },
                Codec = EncodedVideoCodec.H264,
                PresentationTime = context.PresentationTime,
                IsKeyFrame = context.FrameId == 1
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

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
