using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Graphics.Vulkan.Text;

namespace WTK.MediaForge.Windows.Media.Text;

internal sealed class WindowsSystemDrawingFontAtlasRasterizer : IFontAtlasRasterizer
{
    private const int MaxAtlasSide = 4096;
    private const int PaddingPx = 2;

    public FontAtlasAsset Rasterize(
        string text,
        string fontFamily,
        float fontSizePx)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text atlas cannot be created for empty text.", nameof(text));

        if (!float.IsFinite(fontSizePx) || fontSizePx <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSizePx), "Font size must be a positive finite value.");

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows font atlas rasterization requires System.Drawing support on Windows.");

        return RasterizeWindows(text, fontFamily, fontSizePx);
    }

#pragma warning disable CA1416
    private static FontAtlasAsset RasterizeWindows(
        string text,
        string fontFamily,
        float fontSizePx)
    {
        using var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(measureBitmap);
        using var font = CreateFont(fontFamily, fontSizePx);
        using var format = StringFormat.GenericTypographic;

        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var lines = NormalizeLines(text);
        var lineHeight = MathF.Max(1f, font.GetHeight(graphics));
        var maxWidth = 1f;

        foreach (var line in lines)
        {
            var measured = graphics.MeasureString(line.Length == 0 ? " " : line, font, PointF.Empty, format);
            maxWidth = MathF.Max(maxWidth, measured.Width);
        }

        var width = checked((int)MathF.Ceiling(maxWidth + PaddingPx * 2f));
        var height = checked((int)MathF.Ceiling(lines.Length * lineHeight + PaddingPx * 2f));

        if (width <= 0 || height <= 0 || width > MaxAtlasSide || height > MaxAtlasSide)
        {
            throw new InvalidOperationException(
                $"Text atlas for '{fontFamily}' at {fontSizePx:0.###}px is {width}x{height}, outside the supported {MaxAtlasSide}x{MaxAtlasSide} limit.");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var draw = System.Drawing.Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.White))
        {
            draw.Clear(Color.Transparent);
            draw.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            draw.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            for (var i = 0; i < lines.Length; i++)
            {
                draw.DrawString(
                    lines[i],
                    font,
                    brush,
                    new PointF(PaddingPx, PaddingPx + i * lineHeight),
                    format);
            }
        }

        return new FontAtlasAsset
        {
            Text = text,
            FontFamily = fontFamily,
            SizePx = fontSizePx,
            Width = width,
            Height = height,
            AtlasPixels = CopyAlphaAsRgba(bitmap)
        };
    }

    private static Font CreateFont(string fontFamily, float fontSizePx)
    {
        try
        {
            return new Font(fontFamily, fontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            return new Font(TextDrawObjectDefaultFontFamilyFallback.Value, fontSizePx, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }

    private static string[] NormalizeLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static unsafe byte[] CopyAlphaAsRgba(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            var sourceBase = (byte*)data.Scan0;
            var sourceStride = data.Stride;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var sourceRow = sourceBase + y * sourceStride;

                for (var x = 0; x < bitmap.Width; x++)
                {
                    var source = sourceRow + x * 4;
                    var destinationIndex = (y * bitmap.Width + x) * 4;
                    pixels[destinationIndex] = 255;
                    pixels[destinationIndex + 1] = 255;
                    pixels[destinationIndex + 2] = 255;
                    pixels[destinationIndex + 3] = source[3];
                }
            }

            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
#pragma warning restore CA1416

    private static class TextDrawObjectDefaultFontFamilyFallback
    {
        public const string Value = "Segoe UI";
    }
}

