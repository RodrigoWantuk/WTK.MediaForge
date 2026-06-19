namespace WTK.MediaForge.Core.Geometry;

public readonly record struct CanvasPoint(float X, float Y)
{
    public static readonly CanvasPoint Zero = new(0, 0);

    public override string ToString() => $"({X}, {Y})";
}
