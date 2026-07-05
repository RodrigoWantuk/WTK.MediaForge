using Avalonia;

namespace WTK.MediaForge.Studio.Views.Preview;

internal static class PreviewCoordinateMapper
{
    public static Point ClampToCanvas(Point point, double canvasWidth, double canvasHeight)
    {
        return new Point(
            Math.Clamp(point.X, 0, canvasWidth),
            Math.Clamp(point.Y, 0, canvasHeight));
    }
}
