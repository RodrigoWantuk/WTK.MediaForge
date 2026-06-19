namespace WTK.MediaForge.Core.Geometry;

public readonly record struct NormalizedRect(float Left, float Top, float Right, float Bottom)
{
    public static readonly NormalizedRect Full = new(0, 0, 1, 1);

    public float Width => Right - Left;
    public float Height => Bottom - Top;

    public bool IsValid =>
        Left >= 0 &&
        Top >= 0 &&
        Right <= 1 &&
        Bottom <= 1 &&
        Right > Left &&
        Bottom > Top;

    public override string ToString() => $"({Left}, {Top})-({Right}, {Bottom})";
}
