namespace WTK.MediaForge.Core.Geometry;

public readonly struct Transform2D
{
    public CanvasPoint Position { get; init; }

    public CanvasSize Size { get; init; }

    public float RotationDegrees { get; init; }

    public NormalizedPoint Pivot { get; init; }

    public static Transform2D Default => new()
    {
        Position = CanvasPoint.Zero,
        Size = new CanvasSize(100, 100),
        RotationDegrees = 0,
        Pivot = NormalizedPoint.TopLeft
    };

    public bool HasPositiveSize => Size.Width > 0 && Size.Height > 0;

    public override string ToString() =>
        $"pos={Position} size={Size} rot={RotationDegrees}° pivot={Pivot}";
}
