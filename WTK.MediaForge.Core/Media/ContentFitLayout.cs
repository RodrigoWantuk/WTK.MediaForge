namespace WTK.MediaForge.Core.Media;

public readonly struct ContentFitRect : IEquatable<ContentFitRect>
{
    public ContentFitRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public bool Equals(ContentFitRect other) =>
        X == other.X &&
        Y == other.Y &&
        Width == other.Width &&
        Height == other.Height;

    public override bool Equals(object? obj) =>
        obj is ContentFitRect other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Width, Height);
}

public static class ContentFitLayout
{
    public static ContentFitRect ComputeFitRect(
        uint sourceWidth,
        uint sourceHeight,
        uint destinationWidth,
        uint destinationHeight)
    {
        if (sourceWidth == 0 || sourceHeight == 0 || destinationWidth == 0 || destinationHeight == 0)
            return new ContentFitRect(0, 0, (int)destinationWidth, (int)destinationHeight);

        var scale = Math.Min(
            destinationWidth / (double)sourceWidth,
            destinationHeight / (double)sourceHeight);

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var x = (int)((destinationWidth - width) / 2);
        var y = (int)((destinationHeight - height) / 2);

        return new ContentFitRect(x, y, width, height);
    }
}
