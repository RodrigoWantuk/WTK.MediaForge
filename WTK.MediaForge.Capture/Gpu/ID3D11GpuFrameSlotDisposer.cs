namespace WTK.MediaForge.Capture.Gpu;

internal interface ID3D11GpuFrameSlotDisposer
{
    void DisposeSlot(D3D11GpuFrameSlot slot);
}

internal sealed class DefaultD3D11GpuFrameSlotDisposer : ID3D11GpuFrameSlotDisposer
{
    public static DefaultD3D11GpuFrameSlotDisposer Instance { get; } = new();

    public void DisposeSlot(D3D11GpuFrameSlot slot) => slot.Handle.Dispose();
}
