using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public sealed class HostedPreviewSurfaceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task HostedPreviewSurface_attach_valid_surface()
    {
        var outputId = RenderOutputId.New();
        var surface = new TestHostedPreviewSurface();

        await surface.AttachAsync(new HostedPreviewAttachRequest(outputId, Timeout));

        Assert.Equal(HostedPreviewSurfaceState.Attached, surface.State);
        Assert.Equal(outputId, surface.AttachedOutputId);
        Assert.Equal(1, surface.AttachCount);
    }

    [Fact]
    public async Task HostedPreviewSurface_rejects_duplicate_attach()
    {
        var surface = new TestHostedPreviewSurface();
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout)));

        Assert.Equal(HostedPreviewSurfaceState.Attached, surface.State);
    }

    [Fact]
    public async Task HostedPreviewSurface_resize_updates_size_and_dpi_scale()
    {
        var surface = new TestHostedPreviewSurface();
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        var size = new FrameSize(1280, 720);
        var scale = new HostedPreviewDpiScale(1.25f, 1.5f);
        await surface.ResizeAsync(new HostedPreviewResizeRequest(size, scale, Timeout));

        Assert.Equal(size, surface.Size);
        Assert.Equal(scale, surface.DpiScale);
        Assert.Equal(1, surface.ResizeCount);
    }

    [Fact]
    public async Task HostedPreviewSurface_rebind_runs_through_adapter_without_native_handle_contract()
    {
        var surface = new TestHostedPreviewSurface();
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await surface.RebindAsync(new HostedPreviewRebindRequest(Timeout));

        Assert.Equal(1, surface.RebindCount);
    }

    [Fact]
    public async Task HostedPreviewSurface_detach_is_repeatable()
    {
        var surface = new TestHostedPreviewSurface();
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await surface.DetachAsync(new HostedPreviewDetachRequest(Timeout));
        await surface.DetachAsync(new HostedPreviewDetachRequest(Timeout));

        Assert.Equal(HostedPreviewSurfaceState.Detached, surface.State);
        Assert.Null(surface.AttachedOutputId);
        Assert.Equal(1, surface.DetachCount);
    }

    [Fact]
    public async Task HostedPreviewSurface_observes_cancellation_before_attach()
    {
        var surface = new TestHostedPreviewSurface();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout), cts.Token));

        Assert.Equal(HostedPreviewSurfaceState.Detached, surface.State);
    }

    [Fact]
    public async Task HostedPreviewSurface_timeout_preserves_attached_resource()
    {
        var surface = new TestHostedPreviewSurface { ResizeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await surface.ResizeAsync(new HostedPreviewResizeRequest(
                new FrameSize(1920, 1080),
                HostedPreviewDpiScale.One,
                TimeSpan.FromMilliseconds(10))));

        Assert.Equal(HostedPreviewSurfaceState.Attached, surface.State);
        Assert.Null(surface.Size);

        surface.ResizeCompletion.SetResult();
        await Task.Delay(20);
        Assert.Equal(new FrameSize(1920, 1080), surface.Size);
    }

    [Fact]
    public async Task HostedPreviewSurface_close_timeout_preserves_inflight_resource_until_retry()
    {
        var surface = new TestHostedPreviewSurface { CloseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await surface.CloseAsync(new HostedPreviewCloseRequest(TimeSpan.FromMilliseconds(10))));

        Assert.Equal(HostedPreviewSurfaceState.Attached, surface.State);

        surface.CloseCompletion.SetResult();
        await Task.Delay(20);
        await surface.CloseAsync(new HostedPreviewCloseRequest(Timeout));

        Assert.Equal(HostedPreviewSurfaceState.Closed, surface.State);
        Assert.Null(surface.AttachedOutputId);
    }

    [Fact]
    public async Task HostedPreviewSurface_presenter_failure_preserves_attached_state()
    {
        var surface = new TestHostedPreviewSurface { ThrowOnRebind = true };
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await surface.RebindAsync(new HostedPreviewRebindRequest(Timeout)));

        Assert.Equal(HostedPreviewSurfaceState.Attached, surface.State);
    }

    [Fact]
    public async Task HostedPreviewSurface_dispose_closes_deterministically()
    {
        var surface = new TestHostedPreviewSurface();
        await surface.AttachAsync(new HostedPreviewAttachRequest(RenderOutputId.New(), Timeout));

        await surface.DisposeAsync();

        Assert.Equal(HostedPreviewSurfaceState.Closed, surface.State);
        Assert.Equal(1, surface.CloseCount);
    }

    private sealed class TestHostedPreviewSurface : HostedPreviewSurface
    {
        public TestHostedPreviewSurface()
            : base(HostedPreviewSurfaceId.New())
        {
        }

        public int AttachCount { get; private set; }

        public int ResizeCount { get; private set; }

        public int RebindCount { get; private set; }

        public int DetachCount { get; private set; }

        public int CloseCount { get; private set; }

        public TaskCompletionSource? ResizeCompletion { get; set; }

        public TaskCompletionSource? CloseCompletion { get; set; }

        public bool ThrowOnRebind { get; set; }

        protected override RenderOutputTarget CreateRenderOutputTargetCore() =>
            new TestPreviewRenderOutputTarget();

        protected override ValueTask AttachCoreAsync(
            HostedPreviewAttachRequest request,
            CancellationToken cancellationToken)
        {
            AttachCount++;
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask ResizeCoreAsync(
            HostedPreviewResizeRequest request,
            CancellationToken cancellationToken)
        {
            ResizeCount++;
            if (ResizeCompletion is not null)
                await ResizeCompletion.Task;
        }

        protected override ValueTask RebindCoreAsync(
            HostedPreviewRebindRequest request,
            CancellationToken cancellationToken)
        {
            RebindCount++;
            if (ThrowOnRebind)
                throw new InvalidOperationException("Presenter failed.");

            return ValueTask.CompletedTask;
        }

        protected override ValueTask DetachCoreAsync(
            HostedPreviewDetachRequest request,
            CancellationToken cancellationToken)
        {
            DetachCount++;
            return ValueTask.CompletedTask;
        }

        protected override async ValueTask CloseCoreAsync(
            HostedPreviewCloseRequest request,
            CancellationToken cancellationToken)
        {
            CloseCount++;
            if (CloseCompletion is not null)
                await CloseCompletion.Task;
        }
    }

    private sealed class TestPreviewRenderOutputTarget : RenderOutputTarget
    {
        public override RenderOutputTypeId TypeId => RenderOutputTypes.PreviewWindow;
    }
}
