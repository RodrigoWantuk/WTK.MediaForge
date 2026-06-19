namespace WTK.MediaForge.Core.Geometry;

public readonly record struct CanvasSize(float Width, float Height)
{
    public static readonly CanvasSize Empty = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Width}x{Height}";
}
