using WTK.MediaForge.Composition.Assets;

namespace WTK.MediaForge.Graphics.Vulkan.Text;

internal interface IFontAtlasRasterizer
{
    FontAtlasAsset Rasterize(
        string text,
        string fontFamily,
        float fontSizePx);
}

