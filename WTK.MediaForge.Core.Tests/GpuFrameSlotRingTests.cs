using System.Diagnostics;
using WTK.MediaForge.Core.Gpu.Slots;
using Xunit;

namespace WTK.MediaForge.Core.Tests;

public class GpuFrameSlotRingTests
{
    [Fact]
    public void Capture_does_not_write_current_latest_slot()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        var latest = ring.GetLatestSlotIndex();
        Assert.NotNull(latest);

        Assert.True(ring.TryBeginWrite(out var writeIndex));
        Assert.NotEqual(latest, writeIndex);
    }

    [Fact]
    public void Acquire_during_writing_returns_false()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);
        ring.TestForceLatestSlotWriting();

        Assert.False(ring.TryRetainLatest(out _));
    }

    [Fact]
    public void Capture_does_not_overwrite_retained_slot()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var retainedIndex = lease!.SlotIndex;
        var tokenBefore = ring.GetSlotContentToken(retainedIndex);

        Assert.False(ring.TryBeginWrite(out var writeIndex) && writeIndex == retainedIndex);
        Assert.Equal(tokenBefore, ring.GetSlotContentToken(retainedIndex));

        lease.Dispose();
    }

    [Fact]
    public void Replacing_latest_frees_old_when_refcount_zero()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);
        var firstLatest = ring.GetLatestSlotIndex()!.Value;

        CaptureFrame(ring, frameNumber: 2);

        Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(firstLatest));
    }

    [Fact]
    public void Replacing_latest_keeps_old_published_until_last_release()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);
        var firstLatest = ring.GetLatestSlotIndex()!.Value;

        Assert.True(ring.TryRetainLatest(out var lease));

        CaptureFrame(ring, frameNumber: 2);

        Assert.Equal(GpuFrameSlotState.Published, ring.GetSlotState(firstLatest));
        Assert.Equal(1, ring.GetRefCount(firstLatest));

        lease!.Dispose();
        Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(firstLatest));
    }

    [Fact]
    public void Published_generation_is_stable_for_lease()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var generation = lease!.Generation;

        Assert.Equal(generation, lease.Generation);
        Assert.Equal(generation, ring.GetSlotGeneration(lease.SlotIndex));

        lease.Dispose();
    }

    [Fact]
    public void Generation_mismatch_release_throws_in_debug()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var staleGeneration = lease!.Generation - 1;

        Assert.Throws<InvalidOperationException>(() => ring.Release(lease.SlotIndex, staleGeneration));
        Assert.Equal(1, ring.GenerationMismatchCount);

        lease.Dispose();
    }

    [Fact]
    public void Generation_mismatch_release_does_not_free_wrong_frame()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var slotIndex = lease!.SlotIndex;
        var generation = lease.Generation;

        try
        {
            ring.Release(slotIndex, generation - 1);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Equal(1, ring.GetRefCount(slotIndex));
        lease.Dispose();
        Assert.Equal(0, ring.GetRefCount(slotIndex));
    }

    [Fact]
    public void All_slots_retained_capture_drops_frame()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        var leases = new List<GpuFrameSlotLease>();

        for (var frame = 1; frame <= 3; frame++)
        {
            CaptureFrame(ring, frameNumber: frame);
            Assert.True(ring.TryRetainLatest(out var lease));
            leases.Add(lease!);
        }

        var droppedBefore = ring.DroppedFrameCount;
        CaptureFrame(ring, frameNumber: 99);

        Assert.Equal(droppedBefore + 1, ring.DroppedFrameCount);
        Assert.False(ring.TryBeginWrite(out _));

        foreach (var lease in leases)
            lease.Dispose();
    }

    [Fact]
    public void TryRetainLatest_non_blocking_under_pressure()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        var leases = new List<GpuFrameSlotLease>();

        for (var frame = 1; frame <= 3; frame++)
        {
            CaptureFrame(ring, frameNumber: frame);
            Assert.True(ring.TryRetainLatest(out var lease));
            leases.Add(lease!);
        }

        var stopwatch = Stopwatch.StartNew();
        Assert.False(ring.TryBeginWrite(out _));
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 1);

        foreach (var lease in leases)
            lease.Dispose();
    }

    [Fact]
    public void Stop_marks_retained_slots_dispose_pending()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var slotIndex = lease!.SlotIndex;

        ring.Stop();

        Assert.Equal(GpuFrameSlotState.DisposePending, ring.GetSlotState(slotIndex));
        Assert.Equal(1, ring.GetRefCount(slotIndex));

        lease.Dispose();
        Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(slotIndex));
    }

    [Fact]
    public void Stop_try_acquire_returns_false()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        ring.Stop();

        Assert.False(ring.TryRetainLatest(out _));
    }

    [Fact]
    public void RequestFinalize_allows_release_to_complete_deferred_dispose()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);
        CaptureFrame(ring, frameNumber: 1);

        Assert.True(ring.TryRetainLatest(out var lease));
        var slotIndex = lease!.SlotIndex;

        ring.RequestFinalize();

        Assert.False(ring.IsFullyDisposed);
        Assert.Equal(GpuFrameSlotState.DisposePending, ring.GetSlotState(slotIndex));

        lease.Dispose();

        Assert.True(ring.IsFullyDisposed);
        Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(slotIndex));
    }

    [Fact]
    public void CancelWrite_returns_slot_to_free()
    {
        var ring = new GpuFrameSlotRing(slotCount: 3);

        Assert.True(ring.TryBeginWrite(out var slotIndex));
        Assert.Equal(GpuFrameSlotState.Writing, ring.GetSlotState(slotIndex));

        ring.CancelWrite(slotIndex);
        Assert.Equal(GpuFrameSlotState.Free, ring.GetSlotState(slotIndex));
    }

    private static void CaptureFrame(GpuFrameSlotRing ring, long frameNumber)
    {
        if (!ring.TryBeginWrite(out var slotIndex))
            return;

        ring.CompleteWrite(
            slotIndex,
            new FakeGpuFrameSlotHandle
            {
                SlotIndex = slotIndex,
                ContentToken = frameNumber
            },
            frameNumber);
    }
}
