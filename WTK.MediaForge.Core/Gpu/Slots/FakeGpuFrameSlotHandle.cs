namespace WTK.MediaForge.Core.Gpu.Slots;

public sealed class FakeGpuFrameSlotHandle : IGpuFrameHandle
{
    public GpuFrameBackend Backend => GpuFrameBackend.CpuBitmap;

    public int SlotIndex { get; init; }

    public long ContentToken { get; init; }
}
