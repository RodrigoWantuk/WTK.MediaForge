using System.Collections.Immutable;
using Silk.NET.Vulkan;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

public class MediaForgeVulkanRendererTests
{
    [Fact]
    public void Submit_returns_submission_that_completes_via_fence_poll()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var snapshot = CreateEmptySnapshot();
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    WaitUntil(() => submission.IsCompleted, TimeSpan.FromSeconds(5));
                    Assert.True(submission.IsCompleted);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void IsCompleted_is_non_blocking()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var snapshot = CreateEmptySnapshot();
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                    while (!submission.IsCompleted && stopwatch.ElapsedMilliseconds < 5000)
                        Assert.True(submission.IsCompleted || stopwatch.ElapsedMilliseconds < 5000);

                    stopwatch.Stop();
                    Assert.True(stopwatch.ElapsedMilliseconds < 5000);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Submission_dispose_releases_snapshot_leases()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var (snapshot, retainProbe) = CreateSnapshotWithRetainProbe();
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    WaitUntil(() => submission.IsCompleted, TimeSpan.FromSeconds(5));

                    Assert.Equal(1, retainProbe.ActiveRetainCount);
                    submission.DisposeCompleted();
                    Assert.Equal(0, retainProbe.ActiveRetainCount);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Pending_tracker_poll_disposes_completed_vulkan_submission()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var (snapshot, retainProbe) = CreateSnapshotWithRetainProbe();
                var submission = renderer.Backend.Submit(snapshot);

                using var tracker = new PendingRenderSubmissionTracker(maxFramesInFlight: 2);
                tracker.Add(submission);

                WaitUntil(
                    () =>
                    {
                        tracker.PollCompleted();
                        return tracker.PendingCount == 0;
                    },
                    TimeSpan.FromSeconds(5));
                Assert.Equal(0, retainProbe.ActiveRetainCount);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Submit_imports_d3d11_shared_texture_with_keyed_mutex()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            handle.KeyedMutex.AcquireSync(D3D11SharedTextureSyncKeys.Producer, 1000);
            handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var snapshot = CreateSnapshotWithD3D11Frame(handle);
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    WaitUntil(() => submission.IsCompleted, TimeSpan.FromSeconds(5));
                    Assert.True(submission.IsCompleted);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void WaitIdle_completes_outstanding_submissions()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var (snapshot, retainProbe) = CreateSnapshotWithRetainProbe();
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    renderer.Backend.WaitIdle();
                    Assert.True(submission.IsCompleted);
                    submission.DisposeCompleted();
                    Assert.Equal(0, retainProbe.ActiveRetainCount);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Vulkan_submit_updates_handle_key_to_producer_after_successful_submit()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);
            Assert.Equal(D3D11SharedTextureSyncKeys.Consumer, handle.ProducerAcquireKey);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var submission = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));

                try
                {
                    Assert.Equal(D3D11SharedTextureSyncKeys.Producer, handle.ProducerAcquireKey);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Capture_after_successful_queue_submit_uses_producer_key_even_before_fence_completion()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var submission = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));

                try
                {
                    // Key updates immediately after QueueSubmit succeeds, regardless of fence completion.
                    Assert.Equal(D3D11SharedTextureSyncKeys.Producer, handle.ProducerAcquireKey);
                }
                finally
                {
                    ReleaseSubmission(submission);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Second_submit_uses_current_handle_key_and_does_not_timeout()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var first = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));
                ReleaseSubmission(first);

                Assert.Equal(D3D11SharedTextureSyncKeys.Producer, handle.ProducerAcquireKey);

                var second = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));

                try
                {
                    WaitUntil(() => second.IsCompleted, TimeSpan.FromSeconds(5));
                    Assert.True(second.IsCompleted);
                }
                finally
                {
                    ReleaseSubmission(second);
                }
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Capture_can_reuse_handle_after_vulkan_submission_completed()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var submission = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));

                try
                {
                    WaitUntil(() => submission.IsCompleted, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    ReleaseSubmission(submission);
                }

                handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);
                handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
                handle.NotifyCaptureReleasedToConsumer();
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void QueueSubmit_failure_does_not_mark_handle_as_producer()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                renderer.Backend.SimulateQueueSubmitFailure = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                Assert.Equal(D3D11SharedTextureSyncKeys.Consumer, handle.ProducerAcquireKey);
            }
            finally
            {
                renderer.Backend.SimulateQueueSubmitFailure = false;
                guard.Clear();
            }
        }
    }

    private static void SimulateCaptureReleasedToConsumer(D3D11SharedTextureFrameHandle handle)
    {
        handle.KeyedMutex.AcquireSync(D3D11SharedTextureSyncKeys.Producer, 1000);
        handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        handle.NotifyCaptureReleasedToConsumer();
    }

    [Fact]
    public void QueueSubmit_failure_does_not_advance_import_layout()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                renderer.Backend.SimulateQueueSubmitFailure = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                using var lease = renderer.Backend.TextureRegistry.Acquire(handle);
                Assert.Equal(ImageLayout.Undefined, lease.Import.CurrentLayout);
            }
            finally
            {
                renderer.Backend.SimulateQueueSubmitFailure = false;
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Import_layout_starts_as_Undefined()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            using var lease = renderer!.Backend.TextureRegistry.Acquire(handle);
            Assert.Equal(ImageLayout.Undefined, lease.Import.CurrentLayout);
        }
    }

    [Fact]
    public void Cached_import_second_submit_uses_General_to_General_transition()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var first = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));
                ReleaseSubmission(first);

                using (var lease = renderer.Backend.TextureRegistry.Acquire(handle))
                    Assert.Equal(ImageLayout.General, lease.Import.CurrentLayout);

                var second = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));
                ReleaseSubmission(second);

                using (var lease = renderer.Backend.TextureRegistry.Acquire(handle))
                    Assert.Equal(ImageLayout.General, lease.Import.CurrentLayout);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Same_handle_survives_repeated_submits_without_timeout()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (renderer)
        using (device)
        using (handle)
        {
            SimulateCaptureReleasedToConsumer(handle);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                for (var i = 0; i < 100; i++)
                {
                    var submission = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle));
                    ReleaseSubmission(submission);
                }

                Assert.Equal(D3D11SharedTextureSyncKeys.Producer, handle.ProducerAcquireKey);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void WaitIdleAsync_completes_without_device_wait()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        using (renderer)
        {
            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                renderer.Backend.WaitIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None).AsTask().GetAwaiter().GetResult();
                stopwatch.Stop();

                Assert.True(stopwatch.ElapsedMilliseconds < 50);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    private static bool TryCreateRenderer(out TestRendererContext? context)
    {
        context = null;

        try
        {
            var guard = new RenderThreadGuard();
            if (!MediaForgeVulkanRenderer.TryCreate(guard, out var backend) || backend is null)
                return false;

            context = new TestRendererContext(guard, backend);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateSharedTexture(out D3D11GpuDevice device, out D3D11SharedTextureFrameHandle handle)
    {
        device = null!;
        handle = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RenderFrameSnapshot CreateEmptySnapshot() =>
        new()
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480)
                }
            ]
        };

    private static (RenderFrameSnapshot Snapshot, RetainProbe Probe) CreateSnapshotWithRetainProbe()
    {
        var ring = new WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotRing(slotCount: 3);
        ring.TryBeginWrite(out var slotIndex);
        ring.CompleteWrite(
            slotIndex,
            new WTK.MediaForge.Core.Gpu.Slots.FakeGpuFrameSlotHandle { SlotIndex = slotIndex, ContentToken = 1 },
            frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var slotLease));

        var frame = slotLease!.Frame with
        {
            SourceId = SourceId.New(),
            TextureSize = new FrameSize(64, 64),
            LogicalSize = new FrameSize(64, 64)
        };

        var gpuLease = GpuFrameLease.Create(frame, slotLease.Dispose);
        var probe = new RetainProbe(ring, slotIndex);

        var snapshot = new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = new FrameSize(640, 480),
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = frame.SourceId,
                            Transform = new Transform2D { Size = new CanvasSize(640, 480) },
                            BoundFrame = frame
                        }
                    ]
                }
            ],
            FrameLeases = [gpuLease]
        };

        return (snapshot, probe);
    }

    private static RenderFrameSnapshot CreateSnapshotWithD3D11Frame(D3D11SharedTextureFrameHandle handle)
    {
        var frame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = handle,
            TextureSize = handle.TextureSize,
            LogicalSize = handle.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = handle.TextureSize,
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = frame.SourceId,
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(handle.TextureSize.Width, handle.TextureSize.Height)
                            },
                            BoundFrame = frame
                        }
                    ]
                }
            ]
        };
    }

    private static void ReleaseSubmission(IRenderFrameSubmission submission) =>
        submission.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(1);
        }

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class TestRendererContext : IDisposable
    {
        public TestRendererContext(RenderThreadGuard guard, MediaForgeVulkanRenderer backend)
        {
            Guard = guard;
            Backend = backend;
        }

        public RenderThreadGuard Guard { get; }

        public MediaForgeVulkanRenderer Backend { get; }

        public void Dispose() => Backend.Dispose();
    }

    private sealed class RetainProbe
    {
        private readonly WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotRing _ring;
        private readonly int _slotIndex;

        public RetainProbe(WTK.MediaForge.Core.Gpu.Slots.GpuFrameSlotRing ring, int slotIndex)
        {
            _ring = ring;
            _slotIndex = slotIndex;
        }

        public int ActiveRetainCount => _ring.GetRefCount(_slotIndex);
    }
}
