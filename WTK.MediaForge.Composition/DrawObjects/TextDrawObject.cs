using WTK.MediaForge.Core.Color;

namespace WTK.MediaForge.Composition.DrawObjects;

public sealed class TextDrawObject : MediaForgeDrawObject
{
    public const string DefaultFontFamily = "Inter";

    public string Text { get; set; } = string.Empty;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public ColorRgba TextColor { get; set; } = ColorRgba.White;

    public float FontSize { get; set; } = 24f;
}
