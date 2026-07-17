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
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class RenderedOutputEncodingPipelineTests
{
    [Fact]
    public async Task Pipeline_holds_rendered_surface_lease_until_export_completes()
    {
        var outputId = RenderOutputId.New();
        var surface = new TrackingRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
            [surface],
            new RenderFrameContext(
                FrameNumber: 17,
                PresentationTime: TimeSpan.FromMilliseconds(561),
                DeltaTime: TimeSpan.FromMilliseconds(33),
                TargetFps: 30,
                CancellationToken.None));

        var exporter = new BlockingRenderedOutputExporter();
        var packets = new List<EncodedVideoPacket>();
        var encoder = new PreExportedInputRecordingEncoder();
        await using var scheduler = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            packets.Add,
            encodeTimeout: TimeSpan.FromSeconds(1));

        await using var pipeline = new RenderedOutputEncodingPipeline();
        pipeline.RegisterOutput(
            outputId,
            new RenderedOutputEncodeFrameAdapter(exporter),
            scheduler,
            CreateRequirement(),
            new CollectingMediaTransportAuditSink());

        pipeline.PublishCompletedFrames(batch);
        await exporter.WaitUntilExportStartedAsync(TimeSpan.FromSeconds(2));

        Assert.True(batch.HasOutstandingLeases);
        Assert.Equal(0, surface.DisposeCount);

        exporter.ReleaseExport();
        await WaitForConditionAsync(() => packets.Count == 1, TimeSpan.FromSeconds(2));
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
        Assert.Equal(TimeSpan.FromMilliseconds(561), packets[0].PresentationTime);
        Assert.Equal(1, encoder.EncodeAsyncCalls);
    }

    [Fact]
    public async Task Pipeline_reports_backpressure_and_releases_rejected_frame_lease()
    {
        var outputId = RenderOutputId.New();
        var diagnostics = new ListDiagnosticsSink();
        var exporter = new BlockingRenderedOutputExporter();
        var encoder = new PreExportedInputRecordingEncoder();

        await using var scheduler = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            encodeTimeout: TimeSpan.FromSeconds(5));

        await using var pipeline = new RenderedOutputEncodingPipeline(diagnostics);
        pipeline.RegisterOutput(
            outputId,
            new RenderedOutputEncodeFrameAdapter(exporter),
            scheduler,
            CreateRequirement(),
            new CollectingMediaTransportAuditSink(),
            queueCapacity: 1);

        var first = CreateBatch(outputId, frameNumber: 1);
        var second = CreateBatch(outputId, frameNumber: 2);
        var third = CreateBatch(outputId, frameNumber: 3);

        pipeline.PublishCompletedFrames(first);
        await exporter.WaitUntilExportStartedAsync(TimeSpan.FromSeconds(2));
        pipeline.PublishCompletedFrames(second);
        pipeline.PublishCompletedFrames(third);

        await WaitForConditionAsync(
            () => diagnostics.Diagnostics.Any(d => d.Code == "engine.encoding_pipeline_frame_dropped_backpressure"),
            TimeSpan.FromSeconds(2));

        await third.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        exporter.ReleaseExport();
        await first.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
    }

    [Fact]
    public async Task Recording_policy_marks_pipeline_failed_when_export_queue_is_full()
    {
        var outputId = RenderOutputId.New();
        var diagnostics = new ListDiagnosticsSink();
        var exporter = new BlockingRenderedOutputExporter();
        var encoder = new PreExportedInputRecordingEncoder();

        await using var scheduler = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            encodeTimeout: TimeSpan.FromSeconds(5));

        await using var pipeline = new RenderedOutputEncodingPipeline(diagnostics);
        pipeline.RegisterOutput(
            outputId,
            new RenderedOutputEncodeFrameAdapter(exporter),
            scheduler,
            CreateRequirement(),
            new CollectingMediaTransportAuditSink(),
            queueCapacity: 1,
            backpressurePolicy: EncodedOutputBackpressurePolicy.Recording());

        var first = CreateBatch(outputId, frameNumber: 1);
        var second = CreateBatch(outputId, frameNumber: 2);
        var third = CreateBatch(outputId, frameNumber: 3);

        pipeline.PublishCompletedFrames(first);
        await exporter.WaitUntilExportStartedAsync(TimeSpan.FromSeconds(2));
        pipeline.PublishCompletedFrames(second);
        pipeline.PublishCompletedFrames(third);

        await WaitForConditionAsync(
            () => pipeline.TryGetSnapshot(outputId, out var snapshot) &&
                  snapshot.Status == EncodedOutputRuntimeStatus.Failed,
            TimeSpan.FromSeconds(2));

        Assert.True(pipeline.TryGetSnapshot(outputId, out var failedSnapshot));
        Assert.Equal(EncodedOutputRuntimeStatus.Failed, failedSnapshot.Status);
        Assert.Contains("does not allow frame drops", failedSnapshot.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(failedSnapshot.FramesDropped >= 1);
        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Code == "engine.encoding_pipeline_backpressure_failed" &&
                diagnostic.Severity == MediaForgeDiagnosticSeverity.Error);

        exporter.ReleaseExport();
        await first.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await second.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await third.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(0, encoder.EncodeAsyncCalls);
    }

    [Fact]
    public async Task Recording_policy_fatal_export_failure_drains_pending_frames_and_stops_encoding()
    {
        var outputId = RenderOutputId.New();
        var diagnostics = new ListDiagnosticsSink();
        var exporter = new FailingRenderedOutputExporter();
        var encoder = new PreExportedInputRecordingEncoder();

        await using var scheduler = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            new CollectingMediaTransportAuditSink(),
            _ => { },
            encodeTimeout: TimeSpan.FromSeconds(5));

        await using var pipeline = new RenderedOutputEncodingPipeline(diagnostics);
        pipeline.RegisterOutput(
            outputId,
            new RenderedOutputEncodeFrameAdapter(exporter),
            scheduler,
            CreateRequirement(),
            new CollectingMediaTransportAuditSink(),
            queueCapacity: 2,
            backpressurePolicy: EncodedOutputBackpressurePolicy.Recording());

        var first = CreateBatch(outputId, frameNumber: 1);
        var second = CreateBatch(outputId, frameNumber: 2);

        pipeline.PublishCompletedFrames(first);
        await exporter.WaitUntilExportStartedAsync(TimeSpan.FromSeconds(2));
        pipeline.PublishCompletedFrames(second);
        exporter.FailExport();

        await WaitForConditionAsync(
            () => pipeline.TryGetSnapshot(outputId, out var snapshot) &&
                  snapshot.Status == EncodedOutputRuntimeStatus.Failed,
            TimeSpan.FromSeconds(2));

        await first.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await second.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal(0, encoder.EncodeAsyncCalls);
        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Code == "engine.encoding_pipeline_frame_failed" &&
                diagnostic.Severity == MediaForgeDiagnosticSeverity.Error);
    }

    private static RenderedOutputFrameBatch CreateBatch(RenderOutputId outputId, long frameNumber) =>
        RenderedOutputFrameBatch.FromRenderedSurfaces(
            [new TrackingRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180))],
            new RenderFrameContext(
                frameNumber,
                TimeSpan.FromMilliseconds(frameNumber * 33),
                TimeSpan.FromMilliseconds(33),
                30,
                CancellationToken.None));

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

    private sealed class BlockingRenderedOutputExporter : IRenderedOutputEncoderSurfaceExporter
    {
        private readonly TaskCompletionSource _exportStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseExport =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanExport(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement) =>
            surface.BackendSurface is ExportableSurface;

        public async ValueTask<HardwareEncoderInputLease> ExportAsync(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            _exportStarted.TrySetResult();
            await _releaseExport.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
            {
                Width = checked((int)surface.Size.Width),
                Height = checked((int)surface.Size.Height),
                Format = requirement.PixelFormat,
                TransportKind = MediaTransportKind.GpuSurface
            });
        }

        public Task WaitUntilExportStartedAsync(TimeSpan timeout) =>
            _exportStarted.Task.WaitAsync(timeout);

        public void ReleaseExport() => _releaseExport.TrySetResult();
    }

    private sealed class FailingRenderedOutputExporter : IRenderedOutputEncoderSurfaceExporter
    {
        private readonly TaskCompletionSource _exportStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _failExport =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanExport(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement) =>
            surface.BackendSurface is ExportableSurface;

        public async ValueTask<HardwareEncoderInputLease> ExportAsync(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            _exportStarted.TrySetResult();
            await _failExport.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Synthetic export failure.");
        }

        public Task WaitUntilExportStartedAsync(TimeSpan timeout) =>
            _exportStarted.Task.WaitAsync(timeout);

        public void FailExport() => _failExport.TrySetResult();
    }

    private sealed class TrackingRenderedOutputSurfaceLease(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease
    {
        private int _disposeCount;

        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = new ExportableSurface();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ExportableSurface;

    private sealed class PreExportedInputRecordingEncoder : IHardwareVideoEncoder
    {
        private int _encodeAsyncCalls;

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Test pre-export encoder",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test"
        };

        public HardwareEncoderInputRequirement InputRequirement => CreateRequirement();

        public int EncodeAsyncCalls => Volatile.Read(ref _encodeAsyncCalls);

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
            throw new InvalidOperationException("Pipeline should pass a pre-exported encoder input lease.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingGpuFrameExporter : IGpuFrameExporter
    {
        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
            throw new InvalidOperationException("Rendered output pipeline should export surfaces before scheduling.");

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rendered output pipeline should export surfaces before scheduling.");
    }
}
