using Avalonia;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Views.Preview;

internal readonly record struct SceneEditorResizeResult(double X, double Y, double Width, double Height);

internal static class SceneEditorResizeGeometry
{
    public static SceneEditorResizeResult Resize(
        Rect start,
        ResizeHandleKind handle,
        Vector globalDelta,
        double rotationDegrees,
        Point normalizedPivot,
        bool keepAspect,
        bool fromCenter,
        double snap,
        double minimumSize = 16)
    {
        var localDelta = ToLocal(globalDelta, rotationDegrees);
        var changesLeft = handle is ResizeHandleKind.Left or ResizeHandleKind.TopLeft or ResizeHandleKind.BottomLeft;
        var changesRight = handle is ResizeHandleKind.Right or ResizeHandleKind.TopRight or ResizeHandleKind.BottomRight;
        var changesTop = handle is ResizeHandleKind.Top or ResizeHandleKind.TopLeft or ResizeHandleKind.TopRight;
        var changesBottom = handle is ResizeHandleKind.Bottom or ResizeHandleKind.BottomLeft or ResizeHandleKind.BottomRight;

        var widthDelta = changesLeft ? -localDelta.X : changesRight ? localDelta.X : 0;
        var heightDelta = changesTop ? -localDelta.Y : changesBottom ? localDelta.Y : 0;
        if (fromCenter)
        {
            widthDelta *= 2;
            heightDelta *= 2;
        }

        var width = Math.Max(minimumSize, Snap(start.Width + widthDelta, snap));
        var height = Math.Max(minimumSize, Snap(start.Height + heightDelta, snap));
        if (keepAspect && start.Width > 0 && start.Height > 0)
        {
            var aspect = start.Width / start.Height;
            if (Math.Abs(widthDelta / start.Width) >= Math.Abs(heightDelta / start.Height))
                height = Math.Max(minimumSize, width / aspect);
            else
                width = Math.Max(minimumSize, height * aspect);
        }

        var oldAnchor = fromCenter
            ? new Point(start.Width / 2, start.Height / 2)
            : OppositeAnchor(start.Width, start.Height, changesLeft, changesRight, changesTop, changesBottom);
        var newAnchor = fromCenter
            ? new Point(width / 2, height / 2)
            : OppositeAnchor(width, height, changesLeft, changesRight, changesTop, changesBottom);
        var oldPivot = new Point(start.Width * normalizedPivot.X, start.Height * normalizedPivot.Y);
        var newPivot = new Point(width * normalizedPivot.X, height * normalizedPivot.Y);
        var fixedWorld = new Point(start.X, start.Y) + Rotate(oldAnchor - oldPivot, rotationDegrees) + (Vector)oldPivot;
        var origin = fixedWorld - Rotate(newAnchor - newPivot, rotationDegrees) - (Vector)newPivot;

        return new SceneEditorResizeResult(origin.X, origin.Y, width, height);
    }

    internal static Vector ToLocal(Vector globalDelta, double rotationDegrees) =>
        Rotate(globalDelta, -rotationDegrees);

    private static Point OppositeAnchor(double width, double height, bool left, bool right, bool top, bool bottom) =>
        new(left ? width : right ? 0 : width / 2, top ? height : bottom ? 0 : height / 2);

    private static double Snap(double value, double interval) =>
        interval > 0 ? Math.Round(value / interval) * interval : value;

    private static Vector Rotate(Vector value, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Vector(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
    }
}
