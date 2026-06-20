using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Gpu.Slots;

public sealed class GpuFrameSlotLease : IDisposable
{
    private readonly GpuFrameSlotRing _ring;
    private int _disposed;

    internal GpuFrameSlotLease(
        GpuFrameSlotRing ring,
        int slotIndex,
        long generation,
        GpuFrameReference frame)
    {
        _ring = ring;
        SlotIndex = slotIndex;
        Generation = generation;
        Frame = frame;
    }

    public int SlotIndex { get; }

    public long Generation { get; }

    public GpuFrameReference Frame { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _ring.Release(SlotIndex, Generation);
    }
}
