using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Windows.Media.Text;
using Xunit;

namespace WTK.MediaForge.Windows.Tests.Media.Text;

public sealed class WindowsSystemDrawingFontAtlasRasterizerTests
{
    [Fact]
    public void Windows_font_rasterizer_generates_non_placeholder_alpha_mask()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var rasterizer = new WindowsSystemDrawingFontAtlasRasterizer();
        var atlas = rasterizer.Rasterize("I", "Segoe UI", 48f);
        var alphaPixels = CountAlphaPixels(atlas);

        Assert.True(alphaPixels > 0, "Expected rasterized glyph alpha.");
        Assert.True(
            alphaPixels < atlas.Width * atlas.Height / 2,
            $"Expected glyph alpha to cover less than half the atlas, got {alphaPixels} of {atlas.Width * atlas.Height}.");
    }

    private static int CountAlphaPixels(FontAtlasAsset atlas)
    {
        var count = 0;
        for (var i = 3; i < atlas.AtlasPixels.Length; i += 4)
        {
            if (atlas.AtlasPixels[i] != 0)
                count++;
        }

        return count;
    }
}

