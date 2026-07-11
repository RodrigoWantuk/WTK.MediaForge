using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Silk.NET.Vulkan;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal sealed class VulkanFontAtlasBridge : IDisposable
{
    private readonly AssetManager _assetManager;
    private readonly VulkanTextRenderer _textRenderer;
    private readonly VulkanTextAtlasUploader _uploader;
    private bool _disposed;

    public VulkanFontAtlasBridge(
        VulkanHeadlessDevice device,
        AssetManager? assetManager = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _assetManager = assetManager ?? AssetManager.Shared;
        _textRenderer = new VulkanTextRenderer(device);
        _uploader = new VulkanTextAtlasUploader(device);
    }

    internal VulkanTextRenderer TextRenderer => _textRenderer;

    internal VulkanGlyphAtlasCache AtlasCache => _textRenderer.AtlasCacheForTests;

    public bool TryResolveAtlas(
        string text,
        string fontFamily,
        float fontSizePx,
        out GlyphAtlasEntry entry,
        out ImageView imageView)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(fontFamily))
        {
            entry = default;
            imageView = default;
            return false;
        }

        var textKey = CreateTextKey(text, fontFamily, fontSizePx);
        if (_textRenderer.TryGetAtlasEntry(textKey, out entry))
        {
            return _uploader.TryGetImageView(entry.GpuTextureId, out imageView);
        }

        using var fontHandle = _assetManager.LoadFontAtlas(
            text,
            fontFamily,
            fontSizePx,
            () => CreateFontAtlasAsset(text, fontFamily, fontSizePx));

        var uploaded = _uploader.Upload(fontHandle.Value);
        entry = new GlyphAtlasEntry
        {
            Width = fontHandle.Value.Width,
            Height = fontHandle.Value.Height,
            GpuTextureId = uploaded.TextureId
        };

        _textRenderer.StoreAtlasEntry(textKey, entry);
        imageView = uploaded.ImageView;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _uploader.Dispose();
        _textRenderer.Dispose();
    }

    internal static string CreateTextKey(string text, string fontFamily, float fontSizePx) =>
        $"{fontFamily}|{fontSizePx:0.###}|{text}";

    private static FontAtlasAsset CreateFontAtlasAsset(string text, string fontFamily, float fontSizePx) =>
        VulkanFontAtlasRasterizer.Rasterize(text, fontFamily, fontSizePx);
}
