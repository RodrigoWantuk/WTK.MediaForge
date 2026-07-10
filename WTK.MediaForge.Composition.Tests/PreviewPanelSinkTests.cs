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

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task PreviewPanelSink_releases_lease_when_present_fails()
    {
        var outputId = RenderOutputId.New();
        var surface = new FailingPreviewSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
                CancellationToken.None).AsTask());

        Assert.Equal("Preview presentation failed.", error.Message);
        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task PreviewPanelSink_releases_lease_when_present_callback_fails()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(
            panelHandle: 1,
            onFramePresented: (_, _) => ValueTask.FromException(new InvalidOperationException("Callback failed.")));

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
                CancellationToken.None).AsTask());

        Assert.Equal("Callback failed.", error.Message);
        Assert.Equal(1, surface.PresentCount);
        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task PreviewPanelSink_releases_lease_when_called_after_dispose()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);
        await sink.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
                CancellationToken.None).AsTask());

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    [Fact]
    public async Task PreviewPanelSink_start_after_dispose_is_rejected()
    {
        var sink = new PreviewPanelSink(panelHandle: 1);

        await sink.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            sink.StartAsync(CreateContext(RenderOutputId.New()), CancellationToken.None).AsTask());
    }

    private static RenderOutputSinkContext CreateContext(RenderOutputId outputId, FrameSize? size = null) =>
        new(
            outputId,
            size ?? new FrameSize(640, 360),
            RenderPixelFormat.Rgba8Unorm,
            RenderBackendKind.Vulkan);

    private static RenderOutputFrameInfo CreateInfo(
        RenderedOutputFrame frame,
        RenderOutputSinkId sinkId,
        long frameNumber = 1) =>
        new(
            frame.OutputId,
            sinkId,
            frameNumber,
            TimeSpan.Zero,
            frame.Size,
            frame.Format,
            frame.BackendKind);

    [Fact]
    public async Task PreviewPanelSink_dispose_removes_presenter_for_panel()
    {
        var removedHandle = 0L;
        PreviewPanelPresenterLifecycle.RegisterRemovePresentersForPanel(handle =>
            Interlocked.Exchange(ref removedHandle, handle));

        var sink = new PreviewPanelSink(panelHandle: 42);
        await sink.DisposeAsync();

        Assert.Equal(42, removedHandle);
    }

    [Fact]
    public async Task PreviewPanelSink_stop_removes_presenter_for_panel()
    {
        var removedHandle = 0L;
        PreviewPanelPresenterLifecycle.RegisterRemovePresentersForPanel(handle =>
            Interlocked.Exchange(ref removedHandle, handle));

        var sink = new PreviewPanelSink(panelHandle: 77);
        await sink.StopAsync(CancellationToken.None);

        Assert.Equal(77, removedHandle);
    }

    private const int StressCycleCount =
#if DEBUG
        10;
#else
        500;
#endif

    [Fact]
    [Trait("Category", "Stress")]
    public async Task PreviewPanelSink_stress_attach_detach_cycles()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);
        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        for (var cycle = 0; cycle < StressCycleCount; cycle++)
        {
            await sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id, cycle + 1)),
                CancellationToken.None);
        }

        Assert.Equal(StressCycleCount, surface.PresentCount);
        Assert.False(batch.HasOutstandingLeases);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task PreviewPanelSink_stress_resize_cycles()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);
        var resizeCycles = StressCycleCount * 2;
        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        for (var cycle = 0; cycle < resizeCycles; cycle++)
        {
            var size = new FrameSize((uint)(640 + (cycle % 32)), (uint)(360 + (cycle % 24)));
            await sink.StartAsync(CreateContext(outputId, size), CancellationToken.None);
            await sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id, cycle + 1)),
                CancellationToken.None);
        }

        Assert.Equal(resizeCycles, surface.PresentCount);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task PreviewPanelSink_stress_start_stop_cycles()
    {
        var outputId = RenderOutputId.New();
        var surface = new FakePreviewPresentableSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);
        var startStopCycles = Math.Max(StressCycleCount / 2, 10);

        for (var cycle = 0; cycle < startStopCycles; cycle++)
        {
            await sink.StartAsync(CreateContext(outputId), CancellationToken.None);
            await sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id, cycle + 1)),
                CancellationToken.None);
            await sink.StopAsync(CancellationToken.None);
        }

        Assert.Equal(startStopCycles, surface.PresentCount);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task PreviewPanelSink_stress_slow_present_releases_lease()
    {
        var outputId = RenderOutputId.New();
        var surface = new SlowPreviewSurface(outputId, new FrameSize(640, 360), delayMs: 1);
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);
        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        for (var cycle = 0; cycle < StressCycleCount; cycle++)
        {
            await sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id, cycle + 1)),
                CancellationToken.None);
        }

        Assert.Equal(StressCycleCount, surface.PresentCount);
        Assert.False(batch.HasOutstandingLeases);
    }

    [Fact]
    public async Task Preview_present_honors_cancellation_when_acquire_or_fence_wait_blocks()
    {
        var outputId = RenderOutputId.New();
        var surface = new CancellingPreviewSurface(outputId, new FrameSize(640, 360));
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);
        var frame = Assert.Single(batch.Frames);
        var sink = new PreviewPanelSink(panelHandle: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await sink.StartAsync(CreateContext(outputId), CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.OnFrameAsync(
                batch.CreateLease(frame, CreateInfo(frame, sink.Id)),
                cts.Token).AsTask());

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);
    }

    private sealed class CancellingPreviewSurface(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease, IPreviewPresentableRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface => null;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask PresentToWin32PanelAsync(nint panelHandle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromException(new OperationCanceledException(cancellationToken));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

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

    private sealed class SlowPreviewSurface(
        RenderOutputId outputId,
        FrameSize size,
        int delayMs)
        : IRenderedOutputSurfaceLease, IPreviewPresentableRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface => null;

        public int PresentCount => Volatile.Read(ref _presentCount);

        private int _presentCount;

        public async ValueTask PresentToWin32PanelAsync(nint panelHandle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _presentCount);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingPreviewSurface(
        RenderOutputId outputId,
        FrameSize size)
        : IRenderedOutputSurfaceLease, IPreviewPresentableRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface => null;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask PresentToWin32PanelAsync(nint panelHandle, CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("Preview presentation failed."));

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
