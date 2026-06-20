using System.Diagnostics;
using Vortice.DXGI;
using WTK.MediaForge.Capture.Gpu;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Graphics.D3D11;
using Xunit;

namespace WTK.MediaForge.Capture.Tests;

public class D3D11GpuFrameSlotRingTests
{
    [Fact]
    public void Reused_textures_survive_latest_replacement()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var ring = slotRing.Ring;
            var firstHandle = slotRing.GetHandle(0);

            CaptureFrame(slotRing, frameNumber: 1);
            var latestIndex = ring.GetLatestSlotIndex()!.Value;
            var handleBefore = slotRing.GetHandle(latestIndex);

            CaptureFrame(slotRing, frameNumber: 2);

            Assert.Same(handleBefore, slotRing.GetHandle(latestIndex));
            Assert.Same(firstHandle.Texture, handleBefore.Texture);
        }
    }

    [Fact]
    public void Capture_does_not_overwrite_retained_slot()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var ring = slotRing.Ring;
            CaptureFrame(slotRing, frameNumber: 1);

            Assert.True(ring.TryRetainLatest(out var lease));
            var retainedIndex = lease!.SlotIndex;
            var tokenBefore = ring.GetSlotContentToken(retainedIndex);

            CaptureFrame(slotRing, frameNumber: 2);

            Assert.False(ring.TryBeginWrite(out var writeIndex) && writeIndex == retainedIndex);
            Assert.Equal(tokenBefore, ring.GetSlotContentToken(retainedIndex));

            lease.Dispose();
        }
    }

    [Fact]
    public void All_slots_retained_capture_drops_frame()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var ring = slotRing.Ring;
            var leases = new List<GpuFrameSlotLease>();

            for (var frame = 1; frame <= 3; frame++)
            {
                CaptureFrame(slotRing, frameNumber: frame);
                Assert.True(ring.TryRetainLatest(out var lease));
                leases.Add(lease!);
            }

            var droppedBefore = ring.DroppedFrameCount;
            CaptureFrame(slotRing, frameNumber: 99);

            Assert.Equal(droppedBefore + 1, ring.DroppedFrameCount);
            Assert.False(ring.TryBeginWrite(out _));

            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Fact]
    public void Keyed_mutex_per_slot_supports_producer_acquire()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var handle = slotRing.GetHandle(0);

            handle.KeyedMutex.AcquireSync(D3D11SharedTextureSyncKeys.Producer, 1000);
            handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        }
    }

    [Fact]
    public void Stop_try_acquire_returns_false()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var ring = slotRing.Ring;
            CaptureFrame(slotRing, frameNumber: 1);

            ring.Stop();

            Assert.False(ring.TryRetainLatest(out _));
        }
    }

    [Fact]
    public void Stop_marks_retained_slots_dispose_pending()
    {
        if (!TestGpuCaptureSupport.TryCreateDefaultDevice(out var device))
            return;

        using (device)
        using (var slotRing = CreateSlotRing(device))
        {
            var ring = slotRing.Ring;
            CaptureFrame(slotRing, frameNumber: 1);

            Assert.True(ring.TryRetainLatest(out var lease));
            var slotIndex = lease!.SlotIndex;

            ring.Stop();

            Assert.Equal(GpuFrameSlotState.DisposePending, ring.GetSlotState(slotIndex));
            Assert.Equal(1, ring.GetRefCount(slotIndex));

            lease.Dispose();
            Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(slotIndex));
        }
    }

    private static D3D11GpuFrameSlotRing CreateSlotRing(D3D11GpuDevice device) =>
        new(device.Device, width: 64, height: 64, Format.B8G8R8A8_UNorm, slotCount: 3);

    private static void CaptureFrame(D3D11GpuFrameSlotRing slotRing, long frameNumber)
    {
        var ring = slotRing.Ring;

        if (!ring.TryBeginWrite(out var slotIndex))
            return;

        var handle = slotRing.GetHandle(slotIndex);
        var mutexAcquired = false;

        try
        {
            handle.KeyedMutex.AcquireSync(D3D11SharedTextureSyncKeys.Producer, 1000);
            mutexAcquired = true;
        }
        finally
        {
            if (mutexAcquired)
                handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        }

        ring.CompleteWrite(slotIndex, handle, frameNumber, Stopwatch.GetTimestamp());
    }
}
