using System.Collections.Immutable;
using System.Reflection;
using Silk.NET.Vulkan;
using Vortice.DXGI;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
public class MediaForgeVulkanRendererTests
{
    [Fact]
    public void Vulkan_submission_type_does_not_expose_IDisposable_cleanup()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(VulkanRenderFrameSubmission)));
    }

    [Fact]
    public void Vulkan_headless_device_does_not_expose_synchronous_WaitIdle()
    {
        var method = typeof(VulkanHeadlessDevice).GetMethod(
            "WaitIdle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Null(method);
    }

    [Fact]
    public async Task Renderer_Dispose_throws_before_marking_disposed_when_texture_leases_are_active()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (device)
        using (handle)
        {
            var lease = renderer!.Backend.TextureRegistry.Acquire(handle);

            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => renderer.Backend.Dispose());
                Assert.Contains("texture leases are active", ex.Message);

                renderer.Guard.BindToCurrentThread();
                try
                {
                    await renderer.Backend.WaitIdleAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
                }
                finally
                {
                    renderer.Guard.Clear();
                }
            }
            finally
            {
                lease.Dispose();
                renderer.Backend.Dispose();
            }
        }
    }

    [Fact]
    public void Renderer_Dispose_can_be_retried_after_active_lease_is_released()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateSharedTexture(out var device, out var handle))
            return;

        using (device)
        using (handle)
        {
            var lease = renderer!.Backend.TextureRegistry.Acquire(handle);

            Assert.Throws<InvalidOperationException>(() => renderer.Backend.Dispose());

            lease.Dispose();
            renderer.Backend.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                renderer.Backend.TextureRegistry.Acquire(handle));
        }
    }

    [Fact]
    public void Renderer_Dispose_attempts_device_cleanup_even_if_target_dispose_fails()
    {
        var guard = new RenderThreadGuard();
        VulkanHeadlessDevice deviceContext;

        try
        {
            deviceContext = VulkanHeadlessDevice.Create();
        }
        catch
        {
            return;
        }

        var deviceCleanupAttempted = false;
        var renderer = new MediaForgeVulkanRenderer(
            guard,
            deviceContext,
            diagnostics: null,
            NullVulkanRendererFaultInjector.Instance,
            static (_, size) => new ThrowingOffscreenRenderTarget(size),
            () =>
            {
                deviceCleanupAttempted = true;
                deviceContext.Dispose();
            });

        guard.BindToCurrentThread();
        try
        {
            renderer.BindOutput(CreateOffscreenBinding(RenderOutputId.New(), 64, 64));
        }
        finally
        {
            guard.Clear();
        }

        var ex = Assert.Throws<AggregateException>(() => renderer.Dispose());
        Assert.Contains(ex.InnerExceptions, static inner => inner.Message.Contains("target dispose failed"));
        Assert.True(deviceCleanupAttempted);
    }

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
            SimulateCaptureReleasedToConsumer(handle);

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
    public async Task WaitForCompletionAsync_completes_outstanding_submissions()
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
                    await submission.WaitForCompletionAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
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
    public void Submit_with_duplicate_texture_key_acquires_one_registry_lease()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateDevice(out var device))
            return;

        using (renderer)
        using (device)
        {
            var textureId = GpuTextureId.New();
            using var first = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);
            using var second = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);

            SimulateCaptureReleasedToConsumer(first);
            SimulateCaptureReleasedToConsumer(second);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var snapshot = CreateSnapshotWithDuplicateTextureKey(first, second);
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    Assert.Equal(1, renderer.Backend.TextureRegistryActiveLeaseCount);
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
    public void Submit_with_duplicate_texture_key_imports_one_vulkan_texture()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateDevice(out var device))
            return;

        using (renderer)
        using (device)
        {
            var textureId = GpuTextureId.New();
            using var first = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);
            using var second = D3D11SharedTextureFactory.CreateSharedTextureWithTextureId(
                device.Device, 64, 64, textureId);

            SimulateCaptureReleasedToConsumer(first);
            SimulateCaptureReleasedToConsumer(second);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var snapshot = CreateSnapshotWithDuplicateTextureKey(first, second);
                var submission = renderer.Backend.Submit(snapshot);

                try
                {
                    Assert.Equal(1, renderer.Backend.TextureRegistry.EntryCount);
                    Assert.Equal(1, renderer.Backend.TextureRegistryActiveLeaseCount);
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
    public void AcquireTextureLeases_failure_releases_all_previously_acquired_registry_leases()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
            return;

        if (!TryCreateSharedTexture(out var device, out var firstHandle))
            return;

        using (renderer)
        using (device)
        using (firstHandle)
        {
            var secondHandle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);
            var thirdHandle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, width: 64, height: 64);

            using (secondHandle)
            using (thirdHandle)
            {
                SimulateCaptureReleasedToConsumer(firstHandle);
                SimulateCaptureReleasedToConsumer(secondHandle);
                SimulateCaptureReleasedToConsumer(thirdHandle);

                var guard = renderer!.Guard;
                guard.BindToCurrentThread();

                try
                {
                    faultInjector.FailAcquireOnAttempt = 3;

                    Assert.Throws<InvalidOperationException>(() =>
                        renderer.Backend.Submit(CreateSnapshotWithThreeD3D11Frames(firstHandle, secondHandle, thirdHandle)));

                    Assert.Equal(0, renderer.Backend.TextureRegistryActiveLeaseCount);

                    faultInjector.FailAcquireOnAttempt = null;

                    var submission = renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(firstHandle));
                    ReleaseSubmission(submission);

                    Assert.Equal(0, renderer.Backend.TextureRegistryActiveLeaseCount);
                }
                finally
                {
                    faultInjector.FailAcquireOnAttempt = null;
                    guard.Clear();
                }
            }
        }
    }

    [Fact]
    public void QueueSubmit_failure_does_not_mark_handle_as_producer()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
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
                faultInjector.FailQueueSubmit = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                Assert.Equal(D3D11SharedTextureSyncKeys.Consumer, handle.ProducerAcquireKey);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Vulkan_submit_failure_cleanup_runs_once()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
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
                faultInjector.FailQueueSubmit = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                Assert.Equal(1, faultInjector.FailedSubmitCleanupCount);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Vulkan_submit_failure_does_not_double_free_command_buffer()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
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
                faultInjector.FailQueueSubmit = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                Assert.Equal(1, faultInjector.FreedCommandBufferCount);
                Assert.Equal(1, faultInjector.DestroyedFenceCount);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Vulkan_submit_failure_disposes_texture_leases_once()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
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
                faultInjector.FailQueueSubmit = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                Assert.Equal(1, faultInjector.DisposedTextureLeaseCount);
                Assert.Equal(0, renderer.Backend.TextureRegistryActiveLeaseCount);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
                guard.Clear();
            }
        }
    }

    private static void SimulateCaptureReleasedToConsumer(D3D11SharedTextureFrameHandle handle)
    {
        handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);
        handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        handle.NotifyCaptureReleasedToConsumer();
    }

    [Fact]
    public void QueueSubmit_failure_does_not_advance_import_layout()
    {
        var faultInjector = new TestVulkanRendererFaultInjector();

        if (!TryCreateRenderer(out var renderer, faultInjector))
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
                faultInjector.FailQueueSubmit = true;

                Assert.Throws<InvalidOperationException>(() =>
                    renderer.Backend.Submit(CreateSnapshotWithD3D11Frame(handle)));

                using var lease = renderer.Backend.TextureRegistry.Acquire(handle);
                Assert.Equal(ImageLayout.Undefined, lease.Import.CurrentLayout);
            }
            finally
            {
                faultInjector.FailQueueSubmit = false;
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
    public void Cached_import_second_submit_preserves_ShaderReadOnly_layout()
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
                var outputId = RenderOutputId.New();
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 64, 64));

                var first = renderer.Backend.Submit(CreateCp1SnapshotWithD3D11Frame(handle, outputId));
                ReleaseSubmission(first);

                using (var lease = renderer.Backend.TextureRegistry.Acquire(handle))
                    Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);

                var second = renderer.Backend.Submit(CreateCp1SnapshotWithD3D11Frame(handle, outputId));
                ReleaseSubmission(second);

                using (var lease = renderer.Backend.TextureRegistry.Acquire(handle))
                    Assert.Equal(ImageLayout.ShaderReadOnlyOptimal, lease.Import.CurrentLayout);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Vulkan_submission_uses_rendered_surfaces_not_snapshot_frames()
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
                var outputId = RenderOutputId.New();
                renderer.Backend.BindOutput(CreateOffscreenBinding(outputId, 64, 64));

                var submission = renderer.Backend.Submit(CreateCp1SnapshotWithD3D11Frame(handle, outputId));
                var outputFrames = submission.AcquireOutputFrames();
                var frame = Assert.Single(outputFrames.Frames);

                Assert.IsType<VulkanRenderedOutputSurfaceLease>(frame.SurfaceLease);
                Assert.NotNull(frame.SurfaceLease.BackendSurface);

                ReleaseSubmission(submission);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Empty_submit_does_not_change_import_layout()
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
                ReleaseSubmission(submission);

                using var lease = renderer.Backend.TextureRegistry.Acquire(handle);
                Assert.Equal(ImageLayout.Undefined, lease.Import.CurrentLayout);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Same_handle_survives_10_submits_without_timeout()
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
                for (var i = 0; i < 10; i++)
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
    [Trait("Category", TestCategories.Stress)]
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
    public async Task WaitIdleAsync_completes_without_device_wait()
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
                await renderer.Backend.WaitIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                stopwatch.Stop();

                Assert.True(stopwatch.ElapsedMilliseconds < 50);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public void Submit_rejects_more_than_128_external_textures()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateDevice(out var device))
            return;

        using (renderer)
        using (device)
        {
            var handles = new List<D3D11SharedTextureFrameHandle>(129);

            try
            {
                for (var i = 0; i < 129; i++)
                {
                    var handle = D3D11SharedTextureFactory.CreateSharedTexture(device.Device, 64, 64);
                    handles.Add(handle);
                    SimulateCaptureReleasedToConsumer(handle);
                }

                var guard = renderer!.Guard;
                guard.BindToCurrentThread();

                try
                {
                    var snapshot = CreateSnapshotWithManyD3D11Frames(handles);
                    Assert.Throws<NotSupportedException>(() => renderer.Backend.Submit(snapshot));
                }
                finally
                {
                    guard.Clear();
                }
            }
            finally
            {
                foreach (var handle in handles)
                    handle.Dispose();
            }
        }
    }

    [Fact]
    public void MediaForgeVulkanRenderer_is_internal()
    {
        var type = typeof(MediaForgeVulkanRenderer);
        Assert.False(type.IsPublic);
        Assert.True(type.IsNotPublic);
    }

    [Fact]
    public void MediaForgeVulkanRenderBackendFactory_returns_public_backend()
    {
        var guard = new RenderThreadGuard();
        var factory = new MediaForgeVulkanRenderBackendFactory();

        if (!factory.TryCreate(guard, diagnostics: null, out var backend))
            return;

        if (backend is IDisposable disposable)
            disposable.Dispose();

        Assert.NotNull(backend);
        Assert.True(typeof(IRenderBackend).IsAssignableFrom(backend!.GetType()));
        Assert.True(backend.GetType().IsNotPublic);
    }

    private static RenderOutputBindingSnapshot CreateOffscreenBinding(
        RenderOutputId outputId,
        uint width,
        uint height) =>
        new()
        {
            OutputId = outputId,
            TargetKind = RenderTargetKind.Offscreen,
            NativeHandle = 0,
            SurfaceSize = new FrameSize(width, height),
            BindingVersion = 1
        };

    private static RenderFrameSnapshot CreateSnapshotWithManyD3D11Frames(
        IReadOnlyList<D3D11SharedTextureFrameHandle> handles)
    {
        var objects = handles
            .Select((handle, index) => (RenderDrawObjectSnapshot)CreateLayerSnapshot(
                new GpuFrameReference
                {
                    Backend = GpuFrameBackend.D3D11SharedTexture,
                    Handle = handle,
                    TextureSize = handle.TextureSize,
                    LogicalSize = handle.TextureSize,
                    SourceId = SourceId.New(),
                    FrameNumber = index + 1
                },
                $"Layer{index + 1}"))
            .ToArray();

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = handles[0].TextureSize,
                    Objects = [.. objects]
                }
            ]
        };
    }

    private static bool TryCreateRenderer(
        out TestRendererContext? context,
        IVulkanRendererFaultInjector? faultInjector = null)
    {
        context = null;

        try
        {
            var guard = new RenderThreadGuard();
            if (!MediaForgeVulkanRenderer.TryCreate(
                    guard,
                    diagnostics: null,
                    faultInjector ?? NullVulkanRendererFaultInjector.Instance,
                    out var backend) ||
                backend is null)
            {
                return false;
            }

            context = new TestRendererContext(guard, backend);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateDevice(out D3D11GpuDevice device)
    {
        device = null!;

        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Failure || adapter is null)
                return false;

            device = D3D11GpuDevice.CreateForAdapter(adapter);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RenderFrameSnapshot CreateSnapshotWithDuplicateTextureKey(
        D3D11SharedTextureFrameHandle first,
        D3D11SharedTextureFrameHandle second)
    {
        var firstFrame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = first,
            TextureSize = first.TextureSize,
            LogicalSize = first.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 1
        };

        var secondFrame = new GpuFrameReference
        {
            Backend = GpuFrameBackend.D3D11SharedTexture,
            Handle = second,
            TextureSize = second.TextureSize,
            LogicalSize = second.TextureSize,
            SourceId = SourceId.New(),
            FrameNumber = 2
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
                    Size = first.TextureSize,
                    Objects =
                    [
                        CreateLayerSnapshot(firstFrame, "Layer1"),
                        CreateLayerSnapshot(secondFrame, "Layer2")
                    ]
                }
            ]
        };
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

    private static RenderFrameSnapshot CreateSnapshotWithThreeD3D11Frames(
        D3D11SharedTextureFrameHandle first,
        D3D11SharedTextureFrameHandle second,
        D3D11SharedTextureFrameHandle third)
    {
        static GpuFrameReference ToFrame(D3D11SharedTextureFrameHandle handle) =>
            new()
            {
                Backend = GpuFrameBackend.D3D11SharedTexture,
                Handle = handle,
                TextureSize = handle.TextureSize,
                LogicalSize = handle.TextureSize,
                SourceId = SourceId.New(),
                FrameNumber = 1
            };

        var frames = new[] { ToFrame(first), ToFrame(second), ToFrame(third) };

        return new RenderFrameSnapshot
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = first.TextureSize,
                    Objects =
                    [
                        CreateLayerSnapshot(frames[0], "Layer1"),
                        CreateLayerSnapshot(frames[1], "Layer2"),
                        CreateLayerSnapshot(frames[2], "Layer3")
                    ]
                }
            ]
        };
    }

    private static RenderSourceLayerDrawObjectSnapshot CreateLayerSnapshot(GpuFrameReference frame, string name) =>
        new()
        {
            Id = DrawObjectId.New(),
            Name = name,
            SourceId = frame.SourceId,
            Transform = new Transform2D
            {
                Size = new CanvasSize(frame.TextureSize.Width, frame.TextureSize.Height)
            },
            BoundFrame = frame
        };

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

    private static RenderFrameSnapshot CreateCp1SnapshotWithD3D11Frame(
        D3D11SharedTextureFrameHandle handle,
        RenderOutputId outputId)
    {
        var canvasId = CanvasId.New();
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
                    Id = canvasId,
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
            ],
            Outputs =
            [
                new RenderOutputStateSnapshot
                {
                    Id = outputId,
                    Name = "Offscreen",
                    TypeId = RenderOutputTypes.Offscreen,
                    CanvasId = canvasId,
                    OutputSize = handle.TextureSize,
                    CanvasLayoutMode = LayoutMode.Fit
                }
            ]
        };
    }

    private static void ReleaseSubmission(
        IRenderFrameSubmission submission,
        TimeSpan? waitTimeout = null)
    {
        var timeout = waitTimeout ?? TimeSpan.FromSeconds(1);

        try
        {
            submission.WaitForCompletionAsync(timeout, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            submission.DisposeCompleted();
        }
        catch (Exception ex)
        {
            throw new TimeoutException(
                $"Submission did not complete/dispose within {timeout}. " +
                "This usually indicates a Vulkan fence/keyed-mutex synchronization problem.",
                ex);
        }
    }

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

    private sealed class ThrowingOffscreenRenderTarget : IVulkanOffscreenRenderTarget
    {
        public ThrowingOffscreenRenderTarget(FrameSize size)
        {
            Size = size;
        }

        public FrameSize Size { get; private set; }

        public void Resize(FrameSize newSize) => Size = newSize;

        public void Dispose() => throw new InvalidOperationException("target dispose failed");
    }
}
