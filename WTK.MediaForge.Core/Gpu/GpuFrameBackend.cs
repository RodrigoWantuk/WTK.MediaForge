namespace WTK.MediaForge.Core.Gpu;

public enum GpuFrameBackend
{
    Unknown = 0,
    D3D11SharedTexture = 1,
    VulkanImage = 2,
    CpuBitmap = 3
}
