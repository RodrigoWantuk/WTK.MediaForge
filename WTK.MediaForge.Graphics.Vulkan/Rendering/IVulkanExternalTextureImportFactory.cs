using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal interface IVulkanExternalTextureImportFactory
{
    VulkanD3D11TextureImport Import(
        VulkanHeadlessDevice deviceContext,
        D3D11SharedTextureFrameHandle handle);
}

internal sealed class VulkanExternalTextureImportFactory : IVulkanExternalTextureImportFactory
{
    public static VulkanExternalTextureImportFactory Instance { get; } = new();

    private VulkanExternalTextureImportFactory()
    {
    }

    public VulkanD3D11TextureImport Import(
        VulkanHeadlessDevice deviceContext,
        D3D11SharedTextureFrameHandle handle) =>
        VulkanD3D11TextureImport.Import(deviceContext, handle);
}
