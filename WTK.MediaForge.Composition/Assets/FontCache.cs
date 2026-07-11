namespace WTK.MediaForge.Composition.Assets;

internal sealed class FontCache
{
    private readonly object _gate = new();
    private readonly Dictionary<AssetCacheKey, Entry> _entries = new();

    public RefCountedAssetHandle<FontAtlasAsset> Acquire(
        string text,
        string fontFamily,
        float sizePx,
        Func<FontAtlasAsset> factory)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        ArgumentNullException.ThrowIfNull(factory);

        var key = AssetCacheKey.FromString($"{fontFamily}|{sizePx:0.###}|{text}");

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.ReferenceCount++;
                return new RefCountedAssetHandle<FontAtlasAsset>(existing.Asset, Release);
            }

            var asset = factory();
            var entry = new Entry(asset, 1);
            _entries[key] = entry;
            return new RefCountedAssetHandle<FontAtlasAsset>(entry.Asset, Release);
        }
    }

    private void Release(FontAtlasAsset asset)
    {
        lock (_gate)
        {
            Entry? target = null;
            AssetCacheKey targetKey = default;

            foreach (var pair in _entries)
            {
                if (!ReferenceEquals(pair.Value.Asset, asset))
                    continue;

                target = pair.Value;
                targetKey = pair.Key;
                break;
            }

            if (target is null)
                return;

            target.ReferenceCount--;
            if (target.ReferenceCount > 0)
                return;

            _entries.Remove(targetKey);
        }
    }

    private sealed class Entry(FontAtlasAsset asset, int referenceCount)
    {
        public FontAtlasAsset Asset { get; } = asset;

        public int ReferenceCount { get; set; } = referenceCount;
    }
}

internal sealed class FontAtlasAsset
{
    public required string Text { get; init; }

    public required string FontFamily { get; init; }

    public required float SizePx { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required byte[] AtlasPixels { get; init; }
}
