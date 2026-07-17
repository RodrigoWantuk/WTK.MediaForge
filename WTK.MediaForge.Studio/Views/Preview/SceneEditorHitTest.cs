using Avalonia;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.Views.Preview;

public readonly record struct SceneEditorHitResult(LayerItemViewModel? Layer, ResizeHandleKind Handle)
{
    public bool HasLayer => Layer is not null;

    public bool IsResizeHandle => Handle != ResizeHandleKind.None;
}

public static class SceneEditorHitTest
{
    public const double VisibilityToggleSize = 24;

    public static LayerItemViewModel? HitTestLayer(IEnumerable<LayerItemViewModel> layers, Point scenePoint)
    {
        return layers
            .Where(layer => layer.IsVisible && LayerContainsScenePoint(layer, scenePoint))
            .OrderByDescending(layer => layer.Order)
            .FirstOrDefault();
    }

    public static Rect LayerSceneRect(LayerItemViewModel layer)
    {
        return new Rect(layer.X, layer.Y, Math.Max(0, layer.Width), Math.Max(0, layer.Height));
    }

    public static IReadOnlyList<Point> LayerSceneCorners(LayerItemViewModel layer)
    {
        var rect = LayerSceneRect(layer);
        var corners = new[]
        {
            rect.TopLeft,
            rect.TopRight,
            rect.BottomRight,
            rect.BottomLeft
        };

        if (Math.Abs(layer.RotationDegrees) < double.Epsilon)
            return corners;

        return corners
            .Select(point => RotateAround(point, rect.Center, layer.RotationDegrees))
            .ToArray();
    }

    public static IReadOnlyList<Point> LayerViewportCorners(
        LayerItemViewModel layer,
        SceneEditorTransform transform) =>
        LayerSceneCorners(layer)
            .Select(transform.SceneToViewport)
            .ToArray();

    public static bool LayerContainsScenePoint(LayerItemViewModel layer, Point scenePoint)
    {
        var rect = LayerSceneRect(layer);
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        var localPoint = Math.Abs(layer.RotationDegrees) < double.Epsilon
            ? scenePoint
            : RotateAround(scenePoint, rect.Center, -layer.RotationDegrees);

        return rect.Contains(localPoint);
    }

    public static ResizeHandleKind HitTestResizeHandle(Rect viewportLayerRect, Point viewportPoint, double handleSize)
    {
        foreach (var handle in HandleRects(viewportLayerRect, handleSize))
        {
            if (handle.Rect.Contains(viewportPoint))
            {
                return handle.Kind;
            }
        }

        return ResizeHandleKind.None;
    }

    public static ResizeHandleKind HitTestResizeHandle(IReadOnlyList<Point> viewportLayerCorners, Point viewportPoint, double handleSize)
    {
        foreach (var handle in HandleRects(viewportLayerCorners, handleSize))
        {
            if (handle.Rect.Contains(viewportPoint))
            {
                return handle.Kind;
            }
        }

        return ResizeHandleKind.None;
    }

    public static bool HitTestVisibilityToggle(Rect viewportLayerRect, Point viewportPoint)
    {
        return VisibilityToggleRect(viewportLayerRect).Contains(viewportPoint);
    }

    public static bool HitTestVisibilityToggle(IReadOnlyList<Point> viewportLayerCorners, Point viewportPoint)
    {
        return VisibilityToggleRect(viewportLayerCorners).Contains(viewportPoint);
    }

    public static Rect VisibilityToggleRect(Rect viewportLayerRect)
    {
        return new Rect(
            viewportLayerRect.Right - VisibilityToggleSize - 8,
            viewportLayerRect.Top + 8,
            VisibilityToggleSize,
            VisibilityToggleSize);
    }

    public static Rect VisibilityToggleRect(IReadOnlyList<Point> viewportLayerCorners)
    {
        var topRight = viewportLayerCorners.Count >= 2 ? viewportLayerCorners[1] : default;
        return new Rect(
            topRight.X - VisibilityToggleSize - 8,
            topRight.Y + 8,
            VisibilityToggleSize,
            VisibilityToggleSize);
    }

    public static IReadOnlyList<(ResizeHandleKind Kind, Rect Rect)> HandleRects(Rect rect, double handleSize)
    {
        var half = handleSize / 2;
        var left = rect.Left - half;
        var centerX = rect.Center.X - half;
        var right = rect.Right - half;
        var top = rect.Top - half;
        var centerY = rect.Center.Y - half;
        var bottom = rect.Bottom - half;

        return new[]
        {
            (ResizeHandleKind.TopLeft, new Rect(left, top, handleSize, handleSize)),
            (ResizeHandleKind.Top, new Rect(centerX, top, handleSize, handleSize)),
            (ResizeHandleKind.TopRight, new Rect(right, top, handleSize, handleSize)),
            (ResizeHandleKind.Right, new Rect(right, centerY, handleSize, handleSize)),
            (ResizeHandleKind.BottomRight, new Rect(right, bottom, handleSize, handleSize)),
            (ResizeHandleKind.Bottom, new Rect(centerX, bottom, handleSize, handleSize)),
            (ResizeHandleKind.BottomLeft, new Rect(left, bottom, handleSize, handleSize)),
            (ResizeHandleKind.Left, new Rect(left, centerY, handleSize, handleSize))
        };
    }

    public static IReadOnlyList<(ResizeHandleKind Kind, Rect Rect)> HandleRects(IReadOnlyList<Point> corners, double handleSize)
    {
        if (corners.Count < 4)
            return [];

        var topLeft = corners[0];
        var topRight = corners[1];
        var bottomRight = corners[2];
        var bottomLeft = corners[3];
        var top = Midpoint(topLeft, topRight);
        var right = Midpoint(topRight, bottomRight);
        var bottom = Midpoint(bottomLeft, bottomRight);
        var left = Midpoint(topLeft, bottomLeft);

        return new[]
        {
            (ResizeHandleKind.TopLeft, CenteredRect(topLeft, handleSize)),
            (ResizeHandleKind.Top, CenteredRect(top, handleSize)),
            (ResizeHandleKind.TopRight, CenteredRect(topRight, handleSize)),
            (ResizeHandleKind.Right, CenteredRect(right, handleSize)),
            (ResizeHandleKind.BottomRight, CenteredRect(bottomRight, handleSize)),
            (ResizeHandleKind.Bottom, CenteredRect(bottom, handleSize)),
            (ResizeHandleKind.BottomLeft, CenteredRect(bottomLeft, handleSize)),
            (ResizeHandleKind.Left, CenteredRect(left, handleSize))
        };
    }

    private static Point RotateAround(Point point, Point center, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new Point(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d);

    private static Rect CenteredRect(Point center, double size)
    {
        var half = size / 2d;
        return new Rect(center.X - half, center.Y - half, size, size);
    }
}
