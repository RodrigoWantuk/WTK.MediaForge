namespace WTK.MediaForge.Core.Geometry;

public readonly record struct NormalizedPoint(float X, float Y)
{
    public static readonly NormalizedPoint TopLeft = new(0, 0);
    public static readonly NormalizedPoint Center = new(0.5f, 0.5f);

    public override string ToString() => $"({X}, {Y})";
}
