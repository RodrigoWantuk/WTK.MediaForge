using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Capture.Tests;

public class DesktopDuplicationFrameProviderTests
{
    [Fact]
    public async Task Lease_keeps_slot_retained_until_submission_completes()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        using var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(provider);

        var guard = new RenderThreadGuard();
        var backend = new ManualNullRenderBackend(guard);
        using var renderThread = new MediaForgeRenderThread(backend, guard, maxFramesInFlight: 4);
        renderThread.Start();

        renderThread.PublishFrame(BuildSnapshot(runtime, sourceId));
        WaitUntil(() => backend.SubmitCount >= 1, TimeSpan.FromSeconds(5));
        Assert.True(provider.ActiveSlotRetainCount >= 1);

        backend.CompleteAllPending();
        WaitUntil(() => provider.ActiveSlotRetainCount == 0, TimeSpan.FromSeconds(5));

        await provider.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_does_not_destroy_retained_resources()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(provider.TryAcquireLatestFrame(out var lease));
        await provider.StopAsync(CancellationToken.None);

        Assert.True(provider.ActiveSlotRetainCount >= 1);
        lease.Dispose();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_waits_for_retired_rings_after_last_lease()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(provider.TryAcquireLatestFrame(out var lease));
        await provider.StopAsync(CancellationToken.None);
        Assert.Equal(1, provider.RetiredResourceManager.PendingCount);

        lease.Dispose();
        await provider.DisposeAsync();

        Assert.Equal(ProviderDisposeState.Disposed, provider.DisposeState);
        Assert.Equal(0, provider.RetiredResourceManager.PendingCount);
    }

    [Fact]
    public async Task DisposeAsync_times_out_if_lease_never_released()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(provider.TryAcquireLatestFrame(out var lease));
        await provider.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => provider.DisposeAsync().AsTask());

        Assert.Equal(ProviderDisposeState.DisposeTimedOut, provider.DisposeState);
        Assert.Equal(1, provider.RetiredResourceManager.PendingCount);

        lease.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_retry_succeeds_after_lease_released()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(provider.TryAcquireLatestFrame(out var lease));
        await provider.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => provider.DisposeAsync().AsTask());
        Assert.Equal(ProviderDisposeState.DisposeTimedOut, provider.DisposeState);

        lease.Dispose();
        await provider.DisposeAsync();

        Assert.Equal(ProviderDisposeState.Disposed, provider.DisposeState);
        Assert.Equal(0, provider.RetiredResourceManager.PendingCount);
    }

    [Fact]
    public async Task DisposeAsync_concurrent_calls_are_serialized()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);

        var first = provider.DisposeAsync().AsTask();
        var second = provider.DisposeAsync().AsTask();

        await Task.WhenAll(first, second);

        Assert.Equal(ProviderDisposeState.Disposed, provider.DisposeState);
    }

    [Fact]
    public async Task Stop_try_acquire_returns_false()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        using var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));
        await provider.StopAsync(CancellationToken.None);

        Assert.Equal(MediaSourceState.Stopped, provider.State);
        Assert.False(provider.TryAcquireLatestFrame(out _));
    }

    [Fact]
    public async Task Build_deduplicates_leases_for_same_source()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        using var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        var runtime = new CompositionRuntime();
        runtime.RegisterFrameProvider(provider);

        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = captureSource.LogicalSize,
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer A",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        },
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer B",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(320, 240) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        var snapshot = result.TakeSnapshot();

        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.FrameLeases);

        snapshot.Dispose();
        await provider.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Retained_leases_cause_capture_to_drop_when_ring_is_full()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        using var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        await provider.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        var ring = provider.Ring!;
        var retainedLeases = new List<GpuFrameLease>();
        var lastFrameNumber = -1L;

        while (retainedLeases.Count < 3)
        {
            if (!provider.TryAcquireLatestFrame(out var lease))
            {
                await Task.Delay(5);
                continue;
            }

            if (lease.Frame.FrameNumber > lastFrameNumber)
            {
                retainedLeases.Add(lease);
                lastFrameNumber = lease.Frame.FrameNumber;
            }
            else
            {
                lease.Dispose();
            }

            await Task.Delay(5);
        }

        Assert.Equal(3, provider.ActiveSlotRetainCount);

        var droppedBefore = ring.DroppedFrameCount;
        await WaitUntilAsync(() => ring.DroppedFrameCount > droppedBefore, TimeSpan.FromSeconds(5));
        Assert.Equal(3, provider.ActiveSlotRetainCount);

        foreach (var lease in retainedLeases)
            lease.Dispose();

        await provider.StopAsync(CancellationToken.None);
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private static RenderFrameSnapshot BuildSnapshot(CompositionRuntime runtime, SourceId sourceId)
    {
        var projectState = new ProjectStateSnapshot
        {
            Version = 1,
            Canvases =
            [
                new CanvasStateSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new SourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = sourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) }
                        }
                    ]
                }
            ]
        };

        using var result = RenderFrameSnapshotFactory.Build(projectState, runtime);
        return result.TakeSnapshot()!;
    }
}
