using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

/// <summary>
/// GPU text rendering via glyph atlas. CPU generates atlas only when text changes.
/// </summary>
internal sealed class VulkanTextRenderer : IDisposable
{
    private readonly VulkanHeadlessDevice _device;
    private readonly VulkanGlyphAtlasCache _atlasCache;
    private bool _disposed;

    public VulkanTextRenderer(VulkanHeadlessDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _atlasCache = new VulkanGlyphAtlasCache(device);
    }

    public bool TryInvalidateText(string textKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textKey);
        return _atlasCache.Invalidate(textKey);
    }

    public bool TryGetAtlasEntry(string textKey, out GlyphAtlasEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textKey);
        return _atlasCache.TryGet(textKey, out entry);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _atlasCache.Dispose();
    }
}

internal readonly struct GlyphAtlasEntry
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required ulong GpuTextureId { get; init; }
}
