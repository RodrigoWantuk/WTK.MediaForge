namespace WTK.MediaForge.Composition.Sources;

public sealed class FakeGpuFrameHandle : Core.Gpu.IGpuFrameHandle
{
    public Core.Gpu.GpuFrameBackend Backend => Core.Gpu.GpuFrameBackend.CpuBitmap;

    public long Token { get; init; }
}
