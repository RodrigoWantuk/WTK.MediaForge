using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using Xunit;
using PublicRenderOutputSink = WTK.MediaForge.Composition.Outputs.IRenderOutputSink;

namespace WTK.MediaForge.Composition.Tests;

public class RenderOutputSinkDispatcherTests
{
    [Fact]
    public async Task One_output_frame_can_be_consumed_by_two_sinks_and_released_after_both_complete()
    {
        var dispatcher = new RenderOutputSinkDispatcher();
        var output = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Program",
            TypeId = RenderOutputTypes.Offscreen,
            CanvasId = CanvasId.New(),
            OutputSize = new FrameSize(1920, 1080)
        };
        var first = new ControlledSink();
        var second = new ControlledSink();
        var releaseCount = 0;
        var surface = new TrackingRenderedOutputSurfaceLease(
            output.Id,
            output.OutputSize,
            backendSurface: new object());
        var batch = new RenderedOutputFrameBatch(
            [
                new RenderedOutputFrame(
                    output.Id,
                    output.OutputSize,
                    RenderPixelFormat.Rgba8Unorm,
                    RenderBackendKind.Vulkan,
                    surface)
            ],
            () =>
            {
                Interlocked.Increment(ref releaseCount);
                return ValueTask.CompletedTask;
            });

        await dispatcher.AttachAsync(output, first, CancellationToken.None);
        await dispatcher.AttachAsync(output, second, CancellationToken.None);

        try
        {
            dispatcher.PublishCompletedFrames(batch);

            await Task.WhenAll(
                first.WaitForFrameAsync(TimeSpan.FromSeconds(5)),
                second.WaitForFrameAsync(TimeSpan.FromSeconds(5)));

            Assert.True(batch.HasOutstandingLeases);
            Assert.Same(surface, first.ReceivedSurfaceLease);
            Assert.Same(surface, second.ReceivedSurfaceLease);

            first.Release();
            await WaitUntilAsync(() => Volatile.Read(ref releaseCount) == 1, TimeSpan.FromSeconds(5));
            Assert.True(batch.HasOutstandingLeases);
            Assert.Equal(0, surface.DisposeCount);

            second.Release();
            await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.False(batch.HasOutstandingLeases);
            Assert.Equal(2, releaseCount);
            Assert.Equal(1, surface.DisposeCount);
        }
        finally
        {
            first.Release();
            second.Release();
            await dispatcher.DisposeAsync();
        }
    }

    [Fact]
    public async Task RenderedOutputFrame_is_created_from_backend_surface_not_snapshot_only()
    {
        var outputId = RenderOutputId.New();
        var backendSurface = new object();
        var surface = new TrackingRenderedOutputSurfaceLease(
            outputId,
            new FrameSize(640, 480),
            backendSurface);
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces([surface]);

        var frame = Assert.Single(batch.Frames);
        var info = new RenderOutputFrameInfo(
            outputId,
            RenderOutputSinkId.New(),
            frameNumber: 1,
            timestamp: TimeSpan.Zero,
            frame.Size,
            frame.Format,
            frame.BackendKind);
        var lease = batch.CreateLease(frame, info);

        Assert.Same(surface, lease.SurfaceLease);
        Assert.Same(backendSurface, lease.SurfaceLease!.BackendSurface);

        await lease.DisposeAsync();
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, surface.DisposeCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met within the expected timeout.");
    }

    private sealed class ControlledSink : PublicRenderOutputSink
    {
        private readonly TaskCompletionSource _frameEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public IRenderedOutputSurfaceLease? ReceivedSurfaceLease { get; private set; }

        public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
        {
            ReceivedSurfaceLease = frame.SurfaceLease;
            _frameEntered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Release();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }

        public Task WaitForFrameAsync(TimeSpan timeout) =>
            _frameEntered.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }

    private sealed class TrackingRenderedOutputSurfaceLease(
        RenderOutputId outputId,
        FrameSize size,
        object backendSurface)
        : IRenderedOutputSurfaceLease
    {
        public RenderOutputId OutputId { get; } = outputId;

        public FrameSize Size { get; } = size;

        public RenderPixelFormat Format => RenderPixelFormat.Rgba8Unorm;

        public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

        public object? BackendSurface { get; } = backendSurface;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        private int _disposeCount;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
