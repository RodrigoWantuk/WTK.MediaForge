using WTK.MediaForge.Core.Color;

namespace WTK.MediaForge.Composition.DrawObjects;

public sealed class SolidDrawObject : MediaForgeDrawObject
{
    public ColorRgba FillColor { get; set; } = ColorRgba.Black;
}
