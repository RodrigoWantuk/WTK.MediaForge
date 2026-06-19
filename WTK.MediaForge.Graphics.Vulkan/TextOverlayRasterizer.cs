using SkiaSharp;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed class TextOverlayRasterizer
{
    private const float FontSize = 28f;
    private const float Padding = 12f;

    public TextOverlayResult Rasterize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TextOverlayResult.Empty;

        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        using var backgroundPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 180),
            IsAntialias = true
        };

        SKFontMetrics metrics = font.Metrics;
        float textWidth = font.MeasureText(text, textPaint);
        float textHeight = metrics.Descent - metrics.Ascent;

        int width = Math.Max(1, (int)Math.Ceiling(textWidth + Padding * 2));
        int height = Math.Max(1, (int)Math.Ceiling(textHeight + Padding * 2));

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var backgroundRect = new SKRect(0, 0, width, height);
        canvas.DrawRoundRect(backgroundRect, 6f, 6f, backgroundPaint);

        float textY = Padding - metrics.Ascent;
        canvas.DrawText(text, Padding, textY, font, textPaint);

        using SKPixmap pixmap = bitmap.PeekPixels();
        var pixels = pixmap.GetPixelSpan().ToArray();

        return new TextOverlayResult(pixels, (uint)width, (uint)height);
    }
}

public readonly struct TextOverlayResult
{
    public static TextOverlayResult Empty { get; } = new(Array.Empty<byte>(), 1, 1);

    public TextOverlayResult(byte[] pixels, uint width, uint height)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }
    public uint Width { get; }
    public uint Height { get; }
    public bool HasContent => Pixels.Length > 4;
}
