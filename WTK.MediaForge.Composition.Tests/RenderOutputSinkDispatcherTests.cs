using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
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

        await dispatcher.AttachAsync(output, first, TimeSpan.FromSeconds(5), CancellationToken.None);
        await dispatcher.AttachAsync(output, second, TimeSpan.FromSeconds(5), CancellationToken.None);

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

    [Fact]
    public async Task Sink_worker_ignoring_cancellation_does_not_block_detach_forever()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            sinkStopTimeout: TimeSpan.FromMilliseconds(50));
        var output = CreateOutput();
        var sink = new HungWorkerSink();
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
            [
                new TrackingRenderedOutputSurfaceLease(
                    output.Id,
                    output.OutputSize,
                    backendSurface: new object())
            ]);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);
        dispatcher.PublishCompletedFrames(batch);
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        var started = Environment.TickCount64;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            dispatcher.DetachAsync(output.Id, sink.Id, CancellationToken.None));
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

        Assert.True(elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");
        Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));
    }

    [Fact]
    public async Task Sink_worker_ignoring_cancellation_does_not_block_dispatcher_dispose_forever()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            sinkStopTimeout: TimeSpan.FromMilliseconds(50));
        var output = CreateOutput();
        var sink = new HungWorkerSink();
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
            [
                new TrackingRenderedOutputSurfaceLease(
                    output.Id,
                    output.OutputSize,
                    backendSurface: new object())
            ]);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);
        dispatcher.PublishCompletedFrames(batch);
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        var started = Environment.TickCount64;
        await dispatcher.DisposeAsync();
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

        Assert.True(elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");
    }

    [Fact]
    public async Task Dispatcher_dispose_times_out_hung_sink()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            sinkStopTimeout: TimeSpan.FromMilliseconds(50));
        var output = CreateOutput();
        var sink = new HungSink();
        var batch = RenderedOutputFrameBatch.FromRenderedSurfaces(
            [
                new TrackingRenderedOutputSurfaceLease(
                    output.Id,
                    output.OutputSize,
                    backendSurface: new object())
            ]);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);
        dispatcher.PublishCompletedFrames(batch);
        await sink.WaitForFrameAsync(TimeSpan.FromSeconds(5));

        var started = Environment.TickCount64;
        await dispatcher.DisposeAsync();
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

        Assert.True(elapsed < TimeSpan.FromSeconds(2));
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.worker_stop_timeout");

        sink.Release();
    }

    [Fact]
    public async Task AttachSink_start_failure_calls_stop_and_dispose_with_timeout()
    {
        var dispatcher = new RenderOutputSinkDispatcher(
            sinkStopTimeout: TimeSpan.FromSeconds(1));
        var output = CreateOutput();
        var sink = new FailingStartSink();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
        Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));
    }

    [Fact]
    public async Task AttachSink_start_failure_cleanup_timeout_does_not_hang_engine()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            sinkStopTimeout: TimeSpan.FromMilliseconds(50));
        var output = CreateOutput();
        var sink = new FailingStartSink { HangOnStop = true };

        try
        {
            var started = Environment.TickCount64;
            var ex = await Assert.ThrowsAsync<AggregateException>(() =>
                dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None));
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - started);

            Assert.True(elapsed < TimeSpan.FromSeconds(2));
            Assert.Contains(ex.Flatten().InnerExceptions, inner => inner is TimeoutException);
            Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.attach_cleanup_timeout");
            Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));
        }
        finally
        {
            sink.ReleaseStop();
        }
    }

    [Fact]
    public async Task AttachSink_timeout_removes_reserved_registration()
    {
        var dispatcher = new RenderOutputSinkDispatcher(
            sinkStopTimeout: TimeSpan.FromSeconds(1));
        var output = CreateOutput();
        var sink = new HangingStartSink();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            dispatcher.AttachAsync(output, sink, TimeSpan.FromMilliseconds(50), CancellationToken.None));

        Assert.NotNull(ex);
        await WaitUntilAsync(() => sink.StartCancellationObserved, TimeSpan.FromSeconds(5));
        Assert.Equal(1, sink.StopCount);
        Assert.Equal(1, sink.DisposeCount);
        Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));
        Assert.Equal(0, dispatcher.SinkCount);
    }

    [Fact]
    public async Task AttachSink_partial_start_failure_reports_cleanup_diagnostic()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            sinkStopTimeout: TimeSpan.FromSeconds(1));
        var output = CreateOutput();
        var sink = new FailingStartSink
        {
            ThrowOnStop = true,
            ThrowOnDispose = true
        };

        var ex = await Assert.ThrowsAsync<AggregateException>(() =>
            dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None));

        var flattened = ex.Flatten().InnerExceptions;
        Assert.Contains(flattened, inner => inner.Message == "Configured start failure.");
        Assert.Contains(flattened, inner => inner.Message == "Configured stop failure.");
        Assert.Contains(flattened, inner => inner.Message == "Configured dispose failure.");
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.attach_cleanup_failed");
        Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));
    }

    [Fact]
    public async Task PublishCompletedFrames_releases_lease_when_sink_registration_is_stopped()
    {
        RenderOutputSinkDispatcher? dispatcher = null;
        var output = CreateOutput();
        var sink = new ControlledSink();
        var detachOnce = 0;
        dispatcher = new RenderOutputSinkDispatcher(
            beforeDeliveryEnqueue: () =>
            {
                if (Interlocked.Exchange(ref detachOnce, 1) == 0)
                {
                    dispatcher!
                        .DetachAsync(output.Id, sink.Id, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            });
        var batch = CreateTrackedBatch(output, out var surface, out var releaseCount);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, releaseCount());
        Assert.Equal(1, surface.DisposeCount);
        Assert.False(dispatcher.IsSinkAttached(output.Id, sink.Id));

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task PublishCompletedFrames_releases_lease_when_enqueue_fails()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var output = CreateOutput();
        var sink = new ControlledSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            beforeDeliveryEnqueue: () => throw new InvalidOperationException("Configured enqueue failure."));
        var batch = CreateTrackedBatch(output, out var surface, out var releaseCount);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, releaseCount());
        Assert.Equal(1, surface.DisposeCount);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.enqueue_failed");

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task Signal_failure_does_not_double_dispose()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var output = CreateOutput();
        var sink = new ControlledSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            beforeAvailabilitySignal: () => throw new ObjectDisposedException("availability"));
        var batch = CreateTrackedBatch(output, out var surface, out var releaseCount);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, releaseCount());
        Assert.Equal(1, surface.DisposeCount);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.enqueue_signal_failed");

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task TryEnqueue_signal_failure_still_reports_success_to_dispatcher()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var output = CreateOutput();
        var sink = new ControlledSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            beforeAvailabilitySignal: () => throw new ObjectDisposedException("availability"));
        var batch = CreateTrackedBatch(output, out _, out var releaseCount);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, releaseCount());
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.enqueue_signal_failed");
        Assert.DoesNotContain(diagnostics.Diagnostics, diagnostic => diagnostic.Code == "sink.undelivered_frame_dispose_failed");

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task TryEnqueue_signal_failure_releases_frame_lease()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var output = CreateOutput();
        var sink = new ControlledSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            beforeAvailabilitySignal: () => throw new ObjectDisposedException("availability"));
        var batch = CreateTrackedBatch(output, out var surface, out var releaseCount);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(1, releaseCount());
        Assert.Equal(1, surface.DisposeCount);

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task PublishCompletedFrames_releases_lease_when_enqueue_signal_fails()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var output = CreateOutput();
        var sink = new ControlledSink();
        var dispatcher = new RenderOutputSinkDispatcher(
            diagnostics,
            beforeAvailabilitySignal: () => throw new ObjectDisposedException("availability"));
        var batch = CreateTrackedBatch(output, out var surface, out _);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);

        await dispatcher.DisposeAsync();
    }

    [Fact]
    public async Task Detach_race_with_publish_does_not_leak_output_surface_lease()
    {
        RenderOutputSinkDispatcher? dispatcher = null;
        var output = CreateOutput();
        var sink = new ControlledSink();
        var detachOnce = 0;
        dispatcher = new RenderOutputSinkDispatcher(
            beforeDeliveryEnqueue: () =>
            {
                if (Interlocked.Exchange(ref detachOnce, 1) == 0)
                {
                    dispatcher!
                        .DetachAsync(output.Id, sink.Id, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            });
        var batch = CreateTrackedBatch(output, out var surface, out _);

        await dispatcher.AttachAsync(output, sink, TimeSpan.FromSeconds(5), CancellationToken.None);

        dispatcher.PublishCompletedFrames(batch);
        await batch.WaitForLeasesReleasedAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.False(batch.HasOutstandingLeases);
        Assert.Equal(1, surface.DisposeCount);

        await dispatcher.DisposeAsync();
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

    private static MediaForgeRenderOutput CreateOutput() =>
        new()
        {
            Id = RenderOutputId.New(),
            Name = "Program",
            TypeId = RenderOutputTypes.Offscreen,
            CanvasId = CanvasId.New(),
            OutputSize = new FrameSize(1920, 1080)
        };

    private static RenderedOutputFrameBatch CreateTrackedBatch(
        MediaForgeRenderOutput output,
        out TrackingRenderedOutputSurfaceLease surface,
        out Func<int> releaseCount)
    {
        var releases = 0;
        surface = new TrackingRenderedOutputSurfaceLease(
            output.Id,
            output.OutputSize,
            backendSurface: new object());
        releaseCount = () => Volatile.Read(ref releases);

        return new RenderedOutputFrameBatch(
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
                Interlocked.Increment(ref releases);
                return ValueTask.CompletedTask;
            });
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

    private sealed class HungWorkerSink : PublicRenderOutputSink
    {
        private readonly TaskCompletionSource _frameEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
        {
            _frameEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task WaitForFrameAsync(TimeSpan timeout) =>
            _frameEntered.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }

    private sealed class HungSink : PublicRenderOutputSink
    {
        private readonly TaskCompletionSource _frameEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
        {
            _frameEntered.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;

        public Task WaitForFrameAsync(TimeSpan timeout) =>
            _frameEntered.Task.WaitAsync(timeout);

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailingStartSink : PublicRenderOutputSink
    {
        private readonly TaskCompletionSource _stopRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _stopCount;
        private int _disposeCount;

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public bool HangOnStop { get; init; }

        public bool ThrowOnStop { get; init; }

        public bool ThrowOnDispose { get; init; }

        public int StopCount => Volatile.Read(ref _stopCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Configured start failure.");

        public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);

            if (HangOnStop)
                await _stopRelease.Task.ConfigureAwait(false);

            if (ThrowOnStop)
                throw new InvalidOperationException("Configured stop failure.");
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);

            if (ThrowOnDispose)
                throw new InvalidOperationException("Configured dispose failure.");

            return ValueTask.CompletedTask;
        }

        public void ReleaseStop() => _stopRelease.TrySetResult();
    }

    private sealed class HangingStartSink : PublicRenderOutputSink
    {
        private int _stopCount;
        private int _disposeCount;

        public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

        public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

        public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

        public bool StartCancellationObserved { get; private set; }

        public int StopCount => Volatile.Read(ref _stopCount);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public async ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StartCancellationObserved = true;
                throw;
            }
        }

        public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
