namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanExternalTextureLease : IDisposable
{
    private readonly VulkanExternalTextureRegistry.RegistryEntry _entry;
    private readonly VulkanExternalTextureRegistry _registry;
    private int _disposed;

    internal VulkanExternalTextureLease(
        VulkanExternalTextureRegistry.RegistryEntry entry,
        VulkanExternalTextureRegistry registry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal VulkanD3D11TextureImport Import => _entry.Import!;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _registry.Release(_entry.Key);
        _registry.CollectUnused();
    }
}
