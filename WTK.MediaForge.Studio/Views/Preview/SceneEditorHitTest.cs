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
    public static LayerItemViewModel? HitTestLayer(IEnumerable<LayerItemViewModel> layers, Point scenePoint)
    {
        return layers
            .Where(layer => layer.IsVisible && LayerSceneRect(layer).Contains(scenePoint))
            .OrderByDescending(layer => layer.Order)
            .FirstOrDefault();
    }

    public static Rect LayerSceneRect(LayerItemViewModel layer)
    {
        return new Rect(layer.X, layer.Y, Math.Max(0, layer.Width), Math.Max(0, layer.Height));
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
}
