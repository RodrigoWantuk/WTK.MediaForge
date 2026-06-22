using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Capture.Tests;

[Collection("GpuCapture")]
[Trait("Category", TestCategories.Gpu)]
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
        WaitUntil(() => renderThread.PendingTracker.PendingCount == 0, TimeSpan.FromSeconds(5));
        Assert.True(provider.ActiveSlotRetainCount >= 1);
        renderThread.Dispose();
        runtime.Dispose();
        Assert.Equal(0, provider.ActiveSlotRetainCount);

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
        runtime.Dispose();
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

    [Fact]
    public async Task DisposeAsync_sets_DisposeFailed_when_retired_resource_faults()
    {
        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, CreateMinimalCaptureSource());
        provider.AddRetiredResourceForTests(new FaultingRetiredGpuResource());

        var ex = await Assert.ThrowsAsync<AggregateException>(() => provider.DisposeAsync().AsTask());

        Assert.NotNull(ex);
        Assert.Equal(ProviderDisposeState.DisposeFailed, provider.DisposeState);
        Assert.Equal(MediaSourceState.Failed, provider.State);
        Assert.NotNull(provider.LastError);
        Assert.NotEqual(ProviderDisposeState.Disposing, provider.DisposeState);
    }

    [Fact]
    public async Task DisposeAsync_does_not_leave_state_as_Disposing_after_non_timeout_failure()
    {
        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, CreateMinimalCaptureSource());
        provider.AddRetiredResourceForTests(new FaultingRetiredGpuResource());

        await Assert.ThrowsAsync<AggregateException>(() => provider.DisposeAsync().AsTask());

        Assert.NotEqual(ProviderDisposeState.Disposing, provider.DisposeState);
        Assert.Equal(ProviderDisposeState.DisposeFailed, provider.DisposeState);
    }

    [Fact]
    public async Task DisposeAsync_retry_is_allowed_after_DisposeFailed()
    {
        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, CreateMinimalCaptureSource());
        var resource = new RecoverableFaultingRetiredGpuResource();
        provider.AddRetiredResourceForTests(resource);

        await Assert.ThrowsAsync<AggregateException>(() => provider.DisposeAsync().AsTask());
        Assert.Equal(ProviderDisposeState.DisposeFailed, provider.DisposeState);

        resource.AllowFinalize();

        await provider.DisposeAsync();

        Assert.Equal(ProviderDisposeState.Disposed, provider.DisposeState);
        Assert.Equal(0, provider.RetiredResourceManager.PendingCount);
    }

    [Fact]
    public async Task DisposeAsync_reports_diagnostic_on_non_timeout_failure()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(
            sourceId,
            CreateMinimalCaptureSource(),
            diagnostics);
        provider.AddRetiredResourceForTests(new FaultingRetiredGpuResource());

        await Assert.ThrowsAsync<AggregateException>(() => provider.DisposeAsync().AsTask());

        Assert.Contains(
            diagnostics.Diagnostics,
            d => d.Code == "capture.dispose_failed");
    }

    [Fact]
    public async Task StartAsync_throws_when_dispose_in_progress()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);
        provider.AddRetiredResourceForTests(new NeverFinalizingRetiredGpuResource());

        var disposeTask = Task.Run(() => provider.DisposeAsync().AsTask());

        await WaitUntilAsync(
            () => provider.DisposeState == ProviderDisposeState.Disposing,
            TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartAsync(CancellationToken.None));

        provider.RetiredResourceManager.PendingResources
            .OfType<NeverFinalizingRetiredGpuResource>()
            .First()
            .AllowFinalize();

        await disposeTask;
    }

    [Fact]
    public async Task StopAsync_serializes_with_DisposeAsync()
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

        provider.AddRetiredResourceForTests(new NeverFinalizingRetiredGpuResource());

        var disposeTask = provider.DisposeAsync().AsTask();
        var stopTask = provider.StopAsync(CancellationToken.None);

        try
        {
            await Task.WhenAll(disposeTask, stopTask);
        }
        catch (TimeoutException)
        {
        }

        Assert.NotEqual(ProviderDisposeState.Disposing, provider.DisposeState);
        Assert.Equal(ProviderDisposeState.DisposeTimedOut, provider.DisposeState);

        provider.RetiredResourceManager.PendingResources
            .OfType<NeverFinalizingRetiredGpuResource>()
            .First()
            .AllowFinalize();
    }

    [Fact]
    public async Task DisposeAsync_during_StartAsync_does_not_leave_capture_thread_running()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);

        var startTask = provider.StartAsync(CancellationToken.None);
        var disposeTask = provider.DisposeAsync().AsTask();

        await Task.WhenAll(startTask, disposeTask);

        Assert.NotEqual(ProviderDisposeState.Disposing, provider.DisposeState);
        Assert.True(
            provider.DisposeState is ProviderDisposeState.Disposed or ProviderDisposeState.DisposeTimedOut
                or ProviderDisposeState.DisposeFailed);
    }

    [Fact]
    public async Task Concurrent_Start_Stop_Dispose_does_not_leave_provider_in_invalid_state()
    {
        if (!TestGpuCaptureSupport.TryGetPrimaryCaptureSource(out var captureSource))
            return;

        var sourceId = SourceId.New();
        var provider = new DesktopDuplicationFrameProvider(sourceId, captureSource);

        var startTask = provider.StartAsync(CancellationToken.None);
        await startTask;

        await WaitUntilAsync(
            () =>
            {
                if (!provider.TryAcquireLatestFrame(out var probeLease))
                    return false;

                probeLease.Dispose();
                return true;
            },
            TimeSpan.FromSeconds(5));

        var operations = new List<Task>
        {
            provider.StopAsync(CancellationToken.None),
            provider.DisposeAsync().AsTask(),
            provider.StartAsync(CancellationToken.None),
        };

        foreach (var operation in operations)
        {
            try
            {
                await operation;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or ObjectDisposedException
                or TimeoutException
                or AggregateException)
            {
            }
        }

        Assert.NotEqual(ProviderDisposeState.Disposing, provider.DisposeState);
    }

    private static CaptureSourceInfo CreateMinimalCaptureSource() =>
        new()
        {
            AdapterIndex = 0,
            OutputIndex = 0,
            AdapterName = "Test Adapter",
            OutputName = "Test Output",
            AdapterLuid = GpuAdapterLuid.Empty,
            DesktopRect = new DesktopRect(0, 0, 64, 64),
            LogicalSize = new FrameSize(64, 64),
            TextureSize = new FrameSize(64, 64),
            Rotation = DisplayRotation.None,
        };

    private sealed class FaultingRetiredGpuResource : IRetiredGpuResource
    {
        private readonly TaskCompletionSource _fullyDisposedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FullyDisposed => _fullyDisposedTcs.Task;

        public bool TryFinalizePhysicalResources()
        {
            if (_fullyDisposedTcs.Task.IsCompleted)
                return _fullyDisposedTcs.Task.IsCompletedSuccessfully;

            _fullyDisposedTcs.TrySetException(
                new InvalidOperationException("Simulated retired resource finalization failure."));
            return false;
        }
    }

    private sealed class RecoverableFaultingRetiredGpuResource : IRetiredGpuResource
    {
        private TaskCompletionSource _fullyDisposedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _allowFinalize;

        public Task FullyDisposed => _fullyDisposedTcs.Task;

        public void AllowFinalize()
        {
            _allowFinalize = true;

            if (_fullyDisposedTcs.Task.IsFaulted)
            {
                _fullyDisposedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public bool TryFinalizePhysicalResources()
        {
            if (_fullyDisposedTcs.Task.IsCompletedSuccessfully)
                return true;

            if (!_allowFinalize)
            {
                _fullyDisposedTcs.TrySetException(
                    new InvalidOperationException("Simulated retired resource finalization failure."));
                return false;
            }

            _fullyDisposedTcs.TrySetResult();
            return true;
        }
    }

    private sealed class NeverFinalizingRetiredGpuResource : IRetiredGpuResource
    {
        private readonly TaskCompletionSource _fullyDisposedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _allowFinalize;

        public Task FullyDisposed => _fullyDisposedTcs.Task;

        public void AllowFinalize()
        {
            _allowFinalize = true;
            _fullyDisposedTcs.TrySetResult();
        }

        public bool TryFinalizePhysicalResources()
        {
            if (_fullyDisposedTcs.Task.IsCompleted)
                return _fullyDisposedTcs.Task.IsCompletedSuccessfully;

            if (_allowFinalize)
            {
                _fullyDisposedTcs.TrySetResult();
                return true;
            }

            return false;
        }
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
