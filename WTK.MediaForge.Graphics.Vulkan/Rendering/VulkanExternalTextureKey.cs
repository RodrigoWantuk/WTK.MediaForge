using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal readonly record struct VulkanExternalTextureKey(
    GpuTextureId TextureId,
    uint Width,
    uint Height,
    Format Format)
{
    public static VulkanExternalTextureKey From(D3D11SharedTextureFrameHandle handle) =>
        new(
            handle.TextureId,
            handle.TextureSize.Width,
            handle.TextureSize.Height,
            handle.Format);
}
