using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class PreviewPanelSinkTests
{
    [Fact]
    public void PreviewPanelSink_rejects_zero_panel_handle()
    {
        Assert.Throws<ArgumentException>(() => new PreviewPanelSink(panelHandle: 0));
    }

    [Fact]
    public async Task PreviewPanelSink_presents_gpu_frame_and_releases_lease()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);
        await sink.OnFrameAsync(
            batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
            CancellationToken.None);

        Assert.Equal(1, surface.PresentCount);
        Assert.Equal(1, surface.LastPanelHandle);
        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task PreviewPanelSink_requires_vulkan_backend_at_start()
    {
        var sink = new PreviewPanelSink(panelHandle: 1);
        var context = new RenderOutputSinkContext(
            RenderOutputId.New(),
            new FrameSize(640, 360),
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Unknown);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sink.StartAsync(context, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task PreviewPanelSink_rejects_non_presentable_surface()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakeNonPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
                CancellationToken.None).AsTask());
    }

    private static RenderOutputSinkContext CreateContext(RenderOutputId outputId, FrameSize? size = null) =>
        new(
            outputId,
            size ?? new FrameSize(640, 360),
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Vulkan);

    private static RenderOutputFrameInfo CreateInfo(RenderedOutputFrame frame, RenderOutputSinkId sinkId) =>
        new(
            frame.OutputId,
            sinkId,
            frameNumber: 1,
            TimeSpan.Zero,
            frame.Size,
            frame.Format,
            frame.BackendKind);

    private sealed class FakePreviewPresentableSurface(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease, IPreviewPresentableRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface => null;

        public int PresentCount => Volatile.Read(ref _presentCount);

        public nint LastPanelHandle => Volatile.Read(ref _lastPanelHandle);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _presentCount;
        private nint _lastPanelHandle;
        private int _disposeCount;

        public ValueTask PresentToWin32PanelAsync(nint panelHandle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _presentCount);
            Volatile.Write(ref _lastPanelHandle, panelHandle);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeNonPresentableSurface(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
