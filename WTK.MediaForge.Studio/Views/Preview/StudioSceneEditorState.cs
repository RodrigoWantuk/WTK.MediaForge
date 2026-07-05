using Avalonia;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed class SceneEditorTransform
{
    public const double MinZoom = 0.05;
    public const double MaxZoom = 8;

    public double CanvasWidth { get; set; } = 1920;

    public double CanvasHeight { get; set; } = 1080;

    public double ViewportWidth { get; set; }

    public double ViewportHeight { get; set; }

    public double Zoom { get; private set; } = 1;

    public double PanX { get; private set; }

    public double PanY { get; private set; }

    public Point SceneToViewport(Point scene)
    {
        return new Point(scene.X * Zoom + PanX, scene.Y * Zoom + PanY);
    }

    public Point ViewportToScene(Point viewport)
    {
        return new Point((viewport.X - PanX) / Zoom, (viewport.Y - PanY) / Zoom);
    }

    public Rect SceneToViewport(Rect scene)
    {
        var topLeft = SceneToViewport(scene.TopLeft);
        return new Rect(topLeft, new Size(scene.Width * Zoom, scene.Height * Zoom));
    }

    public Rect ViewportToScene(Rect viewport)
    {
        var topLeft = ViewportToScene(viewport.TopLeft);
        var bottomRight = ViewportToScene(viewport.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    public void Fit(double padding)
    {
        if (CanvasWidth <= 0 || CanvasHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            Zoom = 1;
            PanX = 0;
            PanY = 0;
            return;
        }

        var availableWidth = Math.Max(1, ViewportWidth - padding * 2);
        var availableHeight = Math.Max(1, ViewportHeight - padding * 2);
        Zoom = Math.Clamp(Math.Min(availableWidth / CanvasWidth, availableHeight / CanvasHeight), MinZoom, MaxZoom);
        Center();
    }

    public void ZoomAt(Point viewportPoint, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
        {
            return;
        }

        var sceneBefore = ViewportToScene(viewportPoint);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        PanX = viewportPoint.X - sceneBefore.X * Zoom;
        PanY = viewportPoint.Y - sceneBefore.Y * Zoom;
    }

    public void SetZoomAt(Point viewportPoint, double zoom)
    {
        if (zoom <= 0 || double.IsNaN(zoom) || double.IsInfinity(zoom))
        {
            return;
        }

        ZoomAt(viewportPoint, zoom / Zoom);
    }

    public void PanBy(Vector viewportDelta)
    {
        PanX += viewportDelta.X;
        PanY += viewportDelta.Y;
    }

    public void Center()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            PanX = 0;
            PanY = 0;
            return;
        }

        PanX = (ViewportWidth - CanvasWidth * Zoom) / 2;
        PanY = (ViewportHeight - CanvasHeight * Zoom) / 2;
    }
}

public sealed class StudioSceneEditorState
{
    public SceneEditorTransform Transform { get; } = new();

    public SceneEditorSnapSettings Snap { get; } = new();
}
