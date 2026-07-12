using WTK.MediaForge.Composition.Outputs;
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

namespace WTK.MediaForge.Composition.Tests.Scheduling;

public sealed class RenderedOutputEncodeFrameAdapterTests
{
    [Fact]
    public async Task Adapter_accepts_gpu_rendered_surface_and_preserves_frame_context()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: true);
        var frame = new RenderedOutputFrame(
            outputId,
            surface.Size,
            surface.Format,
            surface.BackendKind,
            surface);
        var exporter = new TestRenderedOutputEncoderSurfaceExporter();
        var adapter = new RenderedOutputEncodeFrameAdapter(exporter);
        var context = CreateContext(42);

        using var scheduled = await adapter.CreateScheduledFrameAsync(
            frame,
            context,
            CreateRequirement(),
            new CollectingMediaTransportAuditSink(),
            CancellationToken.None);

        Assert.Same(context, scheduled.Context);
        Assert.NotNull(scheduled.EncoderInputLease);
        Assert.Null(scheduled.TextureLease);
        Assert.True(exporter.ExportCalled);
    }

    [Fact]
    public async Task Adapter_rejects_surface_without_gpu_only_export_path()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: false);
        var frame = new RenderedOutputFrame(
            outputId,
            surface.Size,
            surface.Format,
            surface.BackendKind,
            surface);
        var adapter = new RenderedOutputEncodeFrameAdapter(new TestRenderedOutputEncoderSurfaceExporter());

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await adapter.CreateScheduledFrameAsync(
                frame,
                CreateContext(1),
                CreateRequirement(),
                new CollectingMediaTransportAuditSink(),
                CancellationToken.None));

        Assert.Contains("GPU-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bridge_exports_rendered_output_and_feeds_encode_scheduler_without_texture_exporter()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: true);
        var frame = new RenderedOutputFrame(
            outputId,
            surface.Size,
            surface.Format,
            surface.BackendKind,
            surface);
        var packets = new List<EncodedVideoPacket>();
        var encoder = new PreExportedInputRecordingEncoder();
        var audit = new CollectingMediaTransportAuditSink();

        await using var scheduler = new EncodeSchedulerTarget(
            encoder,
            new ThrowingGpuFrameExporter(),
            audit,
            packets.Add,
            encodeTimeout: TimeSpan.FromSeconds(1));

        var bridge = new RenderedOutputEncodeSchedulerBridge(
            new RenderedOutputEncodeFrameAdapter(new TestRenderedOutputEncoderSurfaceExporter()),
            scheduler,
            CreateRequirement(),
            audit);

        await bridge.SubmitRenderedFrameAsync(
            frame,
            CreateContext(77),
            CancellationToken.None);

        await WaitForConditionAsync(() => packets.Count == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(1, encoder.EncodeAsyncCalls);
        Assert.Equal(0, encoder.SubmitFrameAsyncCalls);
        Assert.Equal(TimeSpan.FromMilliseconds(77 * 33), packets[0].PresentationTime);
    }

    [Fact]
    public async Task Preparer_exports_directly_when_rendered_surface_matches_encoder_requirement()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: true);
        var exporter = new FormatAwareRenderedOutputExporter("B8G8R8A8_UNORM");
        var preparer = new RenderedOutputEncoderInputPreparer(exporter);

        using var lease = await preparer.PrepareAsync(
            surface,
            CreateRequirement("B8G8R8A8_UNORM"),
            new CollectingMediaTransportAuditSink(),
            CancellationToken.None);

        Assert.Equal("B8G8R8A8_UNORM", lease.Descriptor.Format);
        Assert.Equal(1, exporter.ExportCount);
    }

    [Fact]
    public async Task Preparer_exports_source_surface_and_converts_when_encoder_requires_nv12()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: true);
        var exporter = new FormatAwareRenderedOutputExporter("B8G8R8A8_UNORM");
        var converter = new RecordingRenderedOutputInputConverter();
        var preparer = new RenderedOutputEncoderInputPreparer(exporter, converter);
        var audit = new CollectingMediaTransportAuditSink();

        using var lease = await preparer.PrepareAsync(
            surface,
            CreateRequirement("NV12"),
            audit,
            CancellationToken.None);

        Assert.Equal("NV12", lease.Descriptor.Format);
        Assert.Equal(1, exporter.ExportCount);
        Assert.Equal(1, converter.ConvertCount);
        Assert.Contains(
            audit.Events,
            e => e.Kind == MediaTransportAuditEventKind.GpuFormatConversionSucceeded &&
                 e.EvidenceKind == MediaTransportAuditEvidenceKind.BackendCallSucceeded);
    }

    [Fact]
    public async Task Preparer_fails_cleanly_when_gpu_conversion_is_unavailable()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestRenderedOutputSurfaceLease(outputId, new FrameSize(320, 180), canExport: true);
        var exporter = new FormatAwareRenderedOutputExporter("B8G8R8A8_UNORM");
        var preparer = new RenderedOutputEncoderInputPreparer(exporter);
        var audit = new CollectingMediaTransportAuditSink();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await preparer.PrepareAsync(
                surface,
                CreateRequirement("NV12"),
                audit,
                CancellationToken.None));

        Assert.Contains("GPU format conversion", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(audit.Events, e => e.Kind == MediaTransportAuditEventKind.GpuFormatConversionUnavailable);
    }

    private static FrameExecutionContext CreateContext(long frameId) =>
        new()
        {
            FrameId = frameId,
            PresentationTime = TimeSpan.FromMilliseconds(frameId * 33),
            FrameBudget = TimeSpan.FromMilliseconds(33),
            TargetOutputs = []
        };

    private static HardwareEncoderInputRequirement CreateRequirement(string pixelFormat = "B8G8R8A8_UNORM") =>
        new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = pixelFormat,
            RequiresGpuSurface = true
        };

    private sealed class TestRenderedOutputEncoderSurfaceExporter : IRenderedOutputEncoderSurfaceExporter
    {
        public bool ExportCalled { get; private set; }

        public bool CanExport(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement) =>
            surface.BackendSurface is ExportableSurface;

        public ValueTask<HardwareEncoderInputLease> ExportAsync(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportCalled = true;
            return ValueTask.FromResult(HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
            {
                Width = checked((int)surface.Size.Width),
                Height = checked((int)surface.Size.Height),
                Format = requirement.PixelFormat,
                TransportKind = MediaTransportKind.GpuSurface
            }));
        }
    }

    private sealed class FormatAwareRenderedOutputExporter(string exportedFormat) : IRenderedOutputEncoderSurfaceExporter
    {
        public int ExportCount { get; private set; }

        public bool CanExport(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement) =>
            surface.BackendSurface is ExportableSurface &&
            string.Equals(requirement.PixelFormat, exportedFormat, StringComparison.OrdinalIgnoreCase);

        public ValueTask<HardwareEncoderInputLease> ExportAsync(
            IRenderedOutputSurfaceLease surface,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanExport(surface, requirement))
                throw new NotSupportedException("Test exporter cannot export the requested format.");

            ExportCount++;
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
                Source = nameof(FormatAwareRenderedOutputExporter),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
            });
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
                Source = nameof(FormatAwareRenderedOutputExporter),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
            });

            return ValueTask.FromResult(HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
            {
                Width = checked((int)surface.Size.Width),
                Height = checked((int)surface.Size.Height),
                Format = exportedFormat,
                TransportKind = MediaTransportKind.GpuSurface
            }));
        }
    }

    private sealed class RecordingRenderedOutputInputConverter : IRenderedOutputEncoderInputConverter
    {
        private int _convertCount;

        public int ConvertCount => Volatile.Read(ref _convertCount);

        public bool CanConvert(
            HardwareEncoderInputLease source,
            HardwareEncoderInputRequirement requirement) =>
            string.Equals(source.Descriptor.Format, "B8G8R8A8_UNORM", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(requirement.PixelFormat, "NV12", StringComparison.OrdinalIgnoreCase);

        public ValueTask<HardwareEncoderInputLease> ConvertAsync(
            HardwareEncoderInputLease source,
            HardwareEncoderInputRequirement requirement,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanConvert(source, requirement))
                throw new NotSupportedException("Test converter cannot convert the requested format.");

            Interlocked.Increment(ref _convertCount);
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.GpuFormatConversionStarted,
                Source = nameof(RecordingRenderedOutputInputConverter),
                EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly
            });
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.GpuFormatConversionSucceeded,
                Source = nameof(RecordingRenderedOutputInputConverter),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
            });
            auditSink.Record(new MediaTransportAuditEvent
            {
                Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
                Source = nameof(RecordingRenderedOutputInputConverter),
                EvidenceKind = MediaTransportAuditEvidenceKind.BackendCallSucceeded
            });

            return ValueTask.FromResult(HardwareEncoderInputLease.Create(new GpuVideoFrameDescriptor
            {
                Width = requirement.Width,
                Height = requirement.Height,
                Format = requirement.PixelFormat,
                TransportKind = MediaTransportKind.GpuSurface
            }));
        }
    }

    private sealed class TestRenderedOutputSurfaceLease(
        RenderOutputId outputId,
        FrameSize size,
        bool canExport) : IRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = canExport ? new ExportableSurface() : new object();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ExportableSurface;

    private sealed class PreExportedInputRecordingEncoder : IHardwareVideoEncoder
    {
        private int _encodeAsyncCalls;
        private int _submitFrameAsyncCalls;

        public HardwareEncoderInfo Info { get; } = new()
        {
            Name = "Test pre-export encoder",
            Codec = EncodedVideoCodec.H264,
            Backend = "Test"
        };

        public HardwareEncoderInputRequirement InputRequirement => CreateRequirement();

        public int EncodeAsyncCalls => Volatile.Read(ref _encodeAsyncCalls);

        public int SubmitFrameAsyncCalls => Volatile.Read(ref _submitFrameAsyncCalls);

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
            IMediaTransportAuditSink auditSink)
        {
            Interlocked.Increment(ref _submitFrameAsyncCalls);
            throw new InvalidOperationException("Bridge should pass a pre-exported encoder input lease.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingGpuFrameExporter : IGpuFrameExporter
    {
        public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
            throw new InvalidOperationException("Rendered output bridge should export surfaces before scheduling.");

        public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
            GpuVideoFrameDescriptor descriptor,
            IMediaTransportAuditSink auditSink,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Rendered output bridge should export surfaces before scheduling.");
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout)
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
}
