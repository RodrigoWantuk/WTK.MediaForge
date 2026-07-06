namespace WTK.MediaForge.Composition.Assets;

/// <summary>
/// Central asset loading with reference-counted caches.
/// </summary>
public sealed class AssetManager
{
    private readonly TextureCache _textureCache = new();
    private readonly ShaderCache _shaderCache = new();
    private readonly FontCache _fontCache = new();

    public static AssetManager Shared { get; } = new();

    internal TextureCache TextureCache => _textureCache;

    internal ShaderCache ShaderCache => _shaderCache;

    internal FontCache FontCache => _fontCache;

    internal RefCountedAssetHandle<Sources.StaticCpuAsset> LoadTexture(string path) =>
        _textureCache.Acquire(path);

    internal RefCountedAssetHandle<byte[]> LoadShader(string shaderSourceKey, Func<byte[]> factory) =>
        _shaderCache.Acquire(shaderSourceKey, factory);

    internal RefCountedAssetHandle<FontAtlasAsset> LoadFontAtlas(
        string fontFamily,
        float sizePx,
        Func<FontAtlasAsset> factory) =>
        _fontCache.Acquire(fontFamily, sizePx, factory);
}
