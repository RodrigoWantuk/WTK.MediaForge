using System.Collections.Immutable;
using System.Diagnostics;
using Vortice.DXGI;
using WTK.MediaForge.Capture.Gpu;
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

[Trait("Category", TestCategories.Gpu)]
public class GpuRetiredRingVulkanIntegrationTests
{
    [Fact]
    public void Retired_ring_pending_snapshot_can_still_submit_and_cleanup()
    {
        if (!TryCreateRenderer(out var renderer))
            return;

        if (!TryCreateDevice(out var device))
            return;

        using (renderer)
        using (device)
        {
            var manager = new RetiredGpuResourceManager();
            var retiredRing = CreateSlotRing(device.Device);
            CaptureFrame(retiredRing, frameNumber: 1);

            if (!retiredRing.Ring.TryRetainLatest(out var slotLease))
                return;

            var handle = retiredRing.GetHandle(slotLease!.SlotIndex);

            var frame = slotLease.Frame with
            {
                SourceId = SourceId.New(),
                TextureSize = handle.TextureSize,
                LogicalSize = handle.TextureSize
            };

            var gpuLease = GpuFrameLease.Create(frame, () =>
            {
                try
                {
                    slotLease.Dispose();
                }
                finally
                {
                    if (retiredRing.IsRetired)
                        retiredRing.TryFinalizePhysicalResources();

                    manager.TryFinalizeAll();
                }
            });

            var snapshot = CreateSnapshot(frame, gpuLease);

            using var replacementRing = CreateSlotRing(device.Device);
            retiredRing.Retire();
            manager.Add(retiredRing);

            Assert.True(handle.IsRetired);

            var guard = renderer!.Guard;
            guard.BindToCurrentThread();

            try
            {
                var submission = renderer.Backend.Submit(snapshot);
                ReleaseSubmission(submission);

                gpuLease.Dispose();
                manager.TryFinalizeAll();

                Assert.Equal(0, manager.PendingCount);
                Assert.True(retiredRing.FullyDisposed.IsCompletedSuccessfully);

                renderer.Backend.TextureRegistry.CollectUnused();
                Assert.Equal(0, renderer.Backend.TextureRegistry.EntryCount);
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
            if (!MediaForgeVulkanRenderer.TryCreate(
                    guard,
                    diagnostics: null,
                    NullVulkanRendererFaultInjector.Instance,
                    out var backend) ||
                backend is null)
                return false;

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

    private static D3D11GpuFrameSlotRing CreateSlotRing(Vortice.Direct3D11.ID3D11Device device) =>
        new(device, width: 64, height: 64, Format.B8G8R8A8_UNorm, slotCount: 3);

    private static void CaptureFrame(D3D11GpuFrameSlotRing slotRing, long frameNumber)
    {
        var ring = slotRing.Ring;

        if (!ring.TryBeginWrite(out var slotIndex))
            return;

        var handle = slotRing.GetHandle(slotIndex);
        var mutexAcquired = false;

        try
        {
            handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);
            mutexAcquired = true;
        }
        finally
        {
            if (mutexAcquired)
            {
                handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
                handle.NotifyCaptureReleasedToConsumer();
            }
        }

        ring.CompleteWrite(slotIndex, handle, frameNumber, Stopwatch.GetTimestamp());
    }

    private static void SimulateCaptureReleasedToConsumer(D3D11SharedTextureFrameHandle handle)
    {
        handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);
        handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        handle.NotifyCaptureReleasedToConsumer();
    }

    private static RenderFrameSnapshot CreateSnapshot(GpuFrameReference frame, GpuFrameLease gpuLease) =>
        new()
        {
            ProjectStateVersion = 1,
            Canvases =
            [
                new RenderCanvasSnapshot
                {
                    Id = CanvasId.New(),
                    Name = "Main",
                    Size = frame.TextureSize,
                    Objects =
                    [
                        new RenderSourceLayerDrawObjectSnapshot
                        {
                            Id = DrawObjectId.New(),
                            Name = "Layer",
                            SourceId = frame.SourceId,
                            Transform = new Transform2D
                            {
                                Size = new CanvasSize(frame.TextureSize.Width, frame.TextureSize.Height)
                            },
                            BoundFrame = frame
                        }
                    ]
                }
            ],
            FrameLeases = ImmutableArray.Create(gpuLease)
        };

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
}
