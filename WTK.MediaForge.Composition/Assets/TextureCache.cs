using WTK.MediaForge.Composition.Sources;

namespace WTK.MediaForge.Composition.Assets;

internal sealed class TextureCache
{
    private readonly object _gate = new();
    private readonly Dictionary<AssetCacheKey, Entry> _entries = new();
    private readonly IStaticImageAssetDecoder _decoder;

    public TextureCache(IStaticImageAssetDecoder decoder) =>
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));

    internal int LiveEntryCount
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public RefCountedAssetHandle<StaticCpuAsset> Acquire(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var key = AssetCacheKey.FromFile(path);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.ReferenceCount++;
                return new RefCountedAssetHandle<StaticCpuAsset>(existing.Asset, Release);
            }

            var asset = _decoder.Decode(path);
            var entry = new Entry(asset, 1);
            _entries[key] = entry;
            return new RefCountedAssetHandle<StaticCpuAsset>(entry.Asset, Release);
        }
    }

    private void Release(StaticCpuAsset asset)
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

    private sealed class Entry(StaticCpuAsset asset, int referenceCount)
    {
        public StaticCpuAsset Asset { get; } = asset;

        public int ReferenceCount { get; set; } = referenceCount;
    }
}
