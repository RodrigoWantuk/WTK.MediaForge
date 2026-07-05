using System.Collections.Concurrent;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal sealed class VulkanGlyphAtlasCache : IDisposable
{
    private readonly VulkanHeadlessDevice _device;
    private readonly ConcurrentDictionary<string, GlyphAtlasEntry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    public VulkanGlyphAtlasCache(VulkanHeadlessDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public int EntryCount => _entries.Count;

    public bool Invalidate(string textKey) => _entries.TryRemove(textKey, out _);

    public bool TryGet(string textKey, out GlyphAtlasEntry entry) =>
        _entries.TryGetValue(textKey, out entry);

    public void Store(string textKey, GlyphAtlasEntry entry) =>
        _entries[textKey] = entry;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _entries.Clear();
    }
}
