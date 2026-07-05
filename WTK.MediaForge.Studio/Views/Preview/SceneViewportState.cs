using Avalonia;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed class SceneViewportState
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 8;

    public double CanvasWidth { get; set; } = 1920;

    public double CanvasHeight { get; set; } = 1080;

    public double ViewportWidth { get; set; }

    public double ViewportHeight { get; set; }

    public double Zoom { get; private set; } = 1;

    public double OffsetX { get; private set; }

    public double OffsetY { get; private set; }

    public Point ScreenToScene(Point screen)
    {
        return new Point(
            (screen.X - OffsetX) / Zoom,
            (screen.Y - OffsetY) / Zoom);
    }

    public Point SceneToScreen(Point scene)
    {
        return new Point(
            scene.X * Zoom + OffsetX,
            scene.Y * Zoom + OffsetY);
    }

    public void Fit(double padding)
    {
        if (CanvasWidth <= 0 || CanvasHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            Zoom = 1;
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        var availableWidth = Math.Max(1, ViewportWidth - padding * 2);
        var availableHeight = Math.Max(1, ViewportHeight - padding * 2);
        var zoomX = availableWidth / CanvasWidth;
        var zoomY = availableHeight / CanvasHeight;
        Zoom = Math.Clamp(Math.Min(zoomX, zoomY), MinZoom, MaxZoom);
        Center();
    }

    public void ZoomAt(Point screenPoint, double zoomFactor)
    {
        if (zoomFactor <= 0 || double.IsNaN(zoomFactor) || double.IsInfinity(zoomFactor))
        {
            return;
        }

        var sceneBefore = ScreenToScene(screenPoint);
        Zoom = Math.Clamp(Zoom * zoomFactor, MinZoom, MaxZoom);
        OffsetX = screenPoint.X - sceneBefore.X * Zoom;
        OffsetY = screenPoint.Y - sceneBefore.Y * Zoom;
    }

    public void SetZoomAt(Point screenPoint, double zoom)
    {
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom))
        {
            return;
        }

        var factor = zoom / Zoom;
        ZoomAt(screenPoint, factor);
    }

    public void Pan(Vector screenDelta)
    {
        OffsetX += screenDelta.X;
        OffsetY += screenDelta.Y;
    }

    public void Center()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            OffsetX = 0;
            OffsetY = 0;
            return;
        }

        OffsetX = (ViewportWidth - CanvasWidth * Zoom) / 2;
        OffsetY = (ViewportHeight - CanvasHeight * Zoom) / 2;
    }
}
