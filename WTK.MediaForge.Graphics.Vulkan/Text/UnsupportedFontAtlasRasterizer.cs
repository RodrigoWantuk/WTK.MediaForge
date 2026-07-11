using WTK.MediaForge.Composition.Assets;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal sealed class UnsupportedFontAtlasRasterizer : IFontAtlasRasterizer
{
    public static UnsupportedFontAtlasRasterizer Instance { get; } = new();

    private UnsupportedFontAtlasRasterizer()
    {
    }

    public FontAtlasAsset Rasterize(
        string text,
        string fontFamily,
        float fontSizePx) =>
        throw new PlatformNotSupportedException(
            "Text glyph rasterization requires an OS-specific font atlas rasterizer adapter.");
}

