namespace WTK.MediaForge.Composition.Assets;

internal sealed class ShaderCache
{
    private readonly object _gate = new();
    private readonly Dictionary<AssetCacheKey, Entry> _entries = new();

    public RefCountedAssetHandle<byte[]> Acquire(string shaderSourceKey, Func<byte[]> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderSourceKey);
        ArgumentNullException.ThrowIfNull(factory);

        var key = AssetCacheKey.FromString(shaderSourceKey);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.ReferenceCount++;
                return new RefCountedAssetHandle<byte[]>(existing.Bytes, Release);
            }

            var bytes = factory();
            var entry = new Entry(bytes, 1);
            _entries[key] = entry;
            return new RefCountedAssetHandle<byte[]>(entry.Bytes, Release);
        }
    }

    private void Release(byte[] bytes)
    {
        lock (_gate)
        {
            Entry? target = null;
            AssetCacheKey targetKey = default;

            foreach (var pair in _entries)
            {
                if (!ReferenceEquals(pair.Value.Bytes, bytes))
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

    private sealed class Entry(byte[] bytes, int referenceCount)
    {
        public byte[] Bytes { get; } = bytes;

        public int ReferenceCount { get; set; } = referenceCount;
    }
}
