namespace WTK.MediaForge.Core.Geometry;

public readonly record struct CanvasRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public override string ToString() => $"({X}, {Y}, {Width}x{Height})";
}
