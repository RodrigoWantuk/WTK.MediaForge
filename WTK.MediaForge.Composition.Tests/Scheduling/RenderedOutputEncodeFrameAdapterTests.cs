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

    private static FrameExecutionContext CreateContext(long frameId) =>
        new()
        {
            FrameId = frameId,
            PresentationTime = TimeSpan.FromMilliseconds(frameId * 33),
            FrameBudget = TimeSpan.FromMilliseconds(33),
            TargetOutputs = []
        };

    private static HardwareEncoderInputRequirement CreateRequirement() =>
        new()
        {
            Width = 320,
            Height = 180,
            PixelFormat = "B8G8R8A8_UNORM",
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
