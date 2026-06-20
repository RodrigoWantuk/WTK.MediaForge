using Vortice.DXGI;
using SilkVkFormat = Silk.NET.Vulkan.Format;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal static class D3D11VulkanFormatMap
{
    public static SilkVkFormat MapOrThrow(Format dxgiFormat)
    {
        return dxgiFormat switch
        {
            Format.B8G8R8A8_UNorm => SilkVkFormat.B8G8R8A8Unorm,
            _ => throw new NotSupportedException(
                $"DXGI format {dxgiFormat} is not supported for D3D11/Vulkan interop yet.")
        };
    }
}
