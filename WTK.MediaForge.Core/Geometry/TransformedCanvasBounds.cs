namespace WTK.MediaForge.Core.Geometry;

internal readonly record struct TransformedCanvasBounds(
    CanvasRect Bounds,
    CanvasPoint LocalOrigin)
{
    public CanvasSize Size => new(Bounds.Width, Bounds.Height);

    public static bool TryCreate(Transform2D transform, out TransformedCanvasBounds bounds)
    {
        bounds = default;

        if (!IsFinite(transform) || !transform.HasPositiveSize)
            return false;

        var pivotX = transform.Size.Width * transform.Pivot.X;
        var pivotY = transform.Size.Height * transform.Pivot.Y;
        var radians = transform.RotationDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        AddCorner(0f, 0f);
        AddCorner(transform.Size.Width, 0f);
        AddCorner(transform.Size.Width, transform.Size.Height);
        AddCorner(0f, transform.Size.Height);

        if (!float.IsFinite(minX) ||
            !float.IsFinite(minY) ||
            !float.IsFinite(maxX) ||
            !float.IsFinite(maxY) ||
            maxX <= minX ||
            maxY <= minY)
        {
            return false;
        }

        bounds = new TransformedCanvasBounds(
            new CanvasRect(minX, minY, maxX - minX, maxY - minY),
            new CanvasPoint(
                minX - transform.Position.X,
                minY - transform.Position.Y));
        return true;

        void AddCorner(float x, float y)
        {
            var dx = x - pivotX;
            var dy = y - pivotY;
            var rotatedX = pivotX + (dx * cos) - (dy * sin);
            var rotatedY = pivotY + (dx * sin) + (dy * cos);
            var canvasX = transform.Position.X + rotatedX;
            var canvasY = transform.Position.Y + rotatedY;

            minX = MathF.Min(minX, canvasX);
            minY = MathF.Min(minY, canvasY);
            maxX = MathF.Max(maxX, canvasX);
            maxY = MathF.Max(maxY, canvasY);
        }
    }

    private static bool IsFinite(Transform2D transform) =>
        float.IsFinite(transform.Position.X) &&
        float.IsFinite(transform.Position.Y) &&
        float.IsFinite(transform.Size.Width) &&
        float.IsFinite(transform.Size.Height) &&
        float.IsFinite(transform.RotationDegrees) &&
        float.IsFinite(transform.Pivot.X) &&
        float.IsFinite(transform.Pivot.Y);
}
