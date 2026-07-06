namespace WTK.MediaForge.Composition.Assets;

internal readonly record struct AssetCacheKey(string Value)
{
    public static AssetCacheKey FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var lastWrite = File.GetLastWriteTimeUtc(fullPath).Ticks;
        return new AssetCacheKey($"{fullPath}|{lastWrite}");
    }

    public static AssetCacheKey FromString(string value) => new(value);
}
