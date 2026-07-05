using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Views.Components;

public sealed class StudioIcon : Control
{
    public static readonly StyledProperty<StudioIconKind> KindProperty =
        AvaloniaProperty.Register<StudioIcon, StudioIconKind>(nameof(Kind), StudioIconKind.Layer);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StudioIcon, double>(nameof(IconSize), 16);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<StudioIcon, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<StudioIcon, IBrush?>(nameof(Foreground), Brushes.White);

    static StudioIcon()
    {
        AffectsRender<StudioIcon>(KindProperty, IconSizeProperty, StrokeProperty, ForegroundProperty);
        AffectsMeasure<StudioIcon>(IconSizeProperty);
    }

    public StudioIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Math.Max(8, IconSize);
        return new Size(size, size);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        var offsetX = (Bounds.Width - size) / 2;
        var offsetY = (Bounds.Height - size) / 2;
        var rect = new Rect(offsetX + 1.5, offsetY + 1.5, size - 3, size - 3);
        var brush = Stroke ?? Foreground ?? Brushes.White;
        var pen = new Pen(brush, Math.Max(1.25, size / 14), lineCap: PenLineCap.Round);

        switch (Kind)
        {
            case StudioIconKind.Camera:
                DrawCamera(context, rect, pen, brush);
                break;
            case StudioIconKind.Desktop:
            case StudioIconKind.Preview:
                DrawScreen(context, rect, pen);
                break;
            case StudioIconKind.Image:
                DrawImage(context, rect, pen);
                break;
            case StudioIconKind.Text:
                DrawText(context, rect, pen);
                break;
            case StudioIconKind.Video:
                DrawVideo(context, rect, pen);
                break;
            case StudioIconKind.Record:
                context.DrawEllipse(brush, null, rect.Center, rect.Width * 0.32, rect.Height * 0.32);
                break;
            case StudioIconKind.Stream:
                DrawStream(context, rect, pen);
                break;
            case StudioIconKind.Output:
                DrawArrowBox(context, rect, pen);
                break;
            case StudioIconKind.Scene:
                DrawScene(context, rect, pen);
                break;
            case StudioIconKind.Preset:
                DrawPreset(context, rect, pen);
                break;
            case StudioIconKind.Package:
                DrawPackage(context, rect, pen);
                break;
            case StudioIconKind.Eye:
            case StudioIconKind.EyeOff:
                DrawEye(context, rect, pen, brush, Kind == StudioIconKind.EyeOff);
                break;
            case StudioIconKind.Lock:
            case StudioIconKind.Unlock:
                DrawLock(context, rect, pen, Kind == StudioIconKind.Unlock);
                break;
            case StudioIconKind.Add:
                DrawPlus(context, rect, pen);
                break;
            case StudioIconKind.Save:
                DrawSave(context, rect, pen);
                break;
            case StudioIconKind.Open:
                DrawOpen(context, rect, pen);
                break;
            case StudioIconKind.Settings:
                DrawSettings(context, rect, pen);
                break;
            case StudioIconKind.Warning:
                DrawWarning(context, rect, pen);
                break;
            case StudioIconKind.Error:
                DrawError(context, rect, pen);
                break;
            case StudioIconKind.Success:
                DrawSuccess(context, rect, pen);
                break;
            case StudioIconKind.Drag:
                DrawDrag(context, rect, pen);
                break;
            case StudioIconKind.Effect:
                DrawEffect(context, rect, pen);
                break;
            case StudioIconKind.Audio:
                DrawAudio(context, rect, pen);
                break;
            case StudioIconKind.Search:
                DrawSearch(context, rect, pen);
                break;
            case StudioIconKind.Menu:
                DrawMenu(context, rect, pen);
                break;
            case StudioIconKind.Grid:
                DrawGrid(context, rect, pen);
                break;
            case StudioIconKind.SafeArea:
                DrawSafeArea(context, rect, pen);
                break;
            case StudioIconKind.Fit:
                DrawFit(context, rect, pen);
                break;
            case StudioIconKind.ZoomIn:
                DrawSearch(context, rect, pen);
                DrawPlus(context, rect.Deflate(rect.Width * 0.28), pen);
                break;
            case StudioIconKind.ZoomOut:
                DrawSearch(context, rect, pen);
                context.DrawLine(pen, new Point(rect.Center.X - rect.Width * 0.12, rect.Center.Y), new Point(rect.Center.X + rect.Width * 0.12, rect.Center.Y));
                break;
            case StudioIconKind.WindowMinimize:
                context.DrawLine(pen, new Point(rect.X + rect.Width * 0.24, rect.Center.Y), new Point(rect.Right - rect.Width * 0.24, rect.Center.Y));
                break;
            case StudioIconKind.WindowMaximize:
                context.DrawRectangle(null, pen, rect.Deflate(rect.Width * 0.24), 1, 1);
                break;
            case StudioIconKind.WindowClose:
                context.DrawLine(pen, new Point(rect.X + rect.Width * 0.26, rect.Y + rect.Height * 0.26), new Point(rect.Right - rect.Width * 0.26, rect.Bottom - rect.Height * 0.26));
                context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.26, rect.Y + rect.Height * 0.26), new Point(rect.X + rect.Width * 0.26, rect.Bottom - rect.Height * 0.26));
                break;
            case StudioIconKind.Source:
            case StudioIconKind.Layer:
            default:
                DrawLayer(context, rect, pen);
                break;
        }
    }

    private static void DrawCamera(DrawingContext context, Rect rect, Pen pen, IBrush brush)
    {
        context.DrawRectangle(null, pen, new Rect(rect.X, rect.Y + rect.Height * 0.24, rect.Width, rect.Height * 0.58), 3, 3);
        context.DrawEllipse(null, pen, rect.Center, rect.Width * 0.14, rect.Height * 0.14);
        context.DrawRectangle(brush, null, new Rect(rect.X + rect.Width * 0.16, rect.Y + rect.Height * 0.12, rect.Width * 0.24, rect.Height * 0.12), 2, 2);
    }

    private static void DrawScreen(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect.WithHeight(rect.Height * 0.68), 2, 2);
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.68), new Point(rect.Center.X, rect.Bottom));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.28, rect.Bottom), new Point(rect.Right - rect.Width * 0.28, rect.Bottom));
    }

    private static void DrawImage(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 2, 2);
        context.DrawEllipse(null, pen, new Point(rect.X + rect.Width * 0.72, rect.Y + rect.Height * 0.28), rect.Width * 0.07, rect.Height * 0.07);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.15, rect.Bottom - rect.Height * 0.18), new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.58));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.58), new Point(rect.X + rect.Width * 0.58, rect.Bottom - rect.Height * 0.32));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.58, rect.Bottom - rect.Height * 0.32), new Point(rect.Right - rect.Width * 0.12, rect.Bottom - rect.Height * 0.18));
    }

    private static void DrawText(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.18, rect.Y + rect.Height * 0.18), new Point(rect.Right - rect.Width * 0.18, rect.Y + rect.Height * 0.18));
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.18), new Point(rect.Center.X, rect.Bottom - rect.Height * 0.14));
        context.DrawLine(pen, new Point(rect.Center.X - rect.Width * 0.18, rect.Bottom - rect.Height * 0.14), new Point(rect.Center.X + rect.Width * 0.18, rect.Bottom - rect.Height * 0.14));
    }

    private static void DrawVideo(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect.WithWidth(rect.Width * 0.68), 2, 2);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.68, rect.Y + rect.Height * 0.34), new Point(rect.Right, rect.Y + rect.Height * 0.18));
        context.DrawLine(pen, new Point(rect.Right, rect.Y + rect.Height * 0.18), new Point(rect.Right, rect.Bottom - rect.Height * 0.18));
        context.DrawLine(pen, new Point(rect.Right, rect.Bottom - rect.Height * 0.18), new Point(rect.X + rect.Width * 0.68, rect.Bottom - rect.Height * 0.34));
    }

    private static void DrawStream(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawEllipse(null, pen, rect.Center, rect.Width * 0.09, rect.Height * 0.09);
        context.DrawArc(pen, rect.Deflate(rect.Width * 0.18), 315, 90);
        context.DrawArc(pen, rect.Deflate(rect.Width * 0.02), 315, 90);
        context.DrawArc(pen, rect.Deflate(rect.Width * 0.18), 135, 90);
        context.DrawArc(pen, rect.Deflate(rect.Width * 0.02), 135, 90);
    }

    private static void DrawArrowBox(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect.Deflate(rect.Width * 0.08), 2, 2);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.28, rect.Center.Y), new Point(rect.Right - rect.Width * 0.2, rect.Center.Y));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.2, rect.Center.Y), new Point(rect.Right - rect.Width * 0.34, rect.Y + rect.Height * 0.35));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.2, rect.Center.Y), new Point(rect.Right - rect.Width * 0.34, rect.Bottom - rect.Height * 0.35));
    }

    private static void DrawScene(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 2, 2);
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.14, rect.Y + rect.Height * 0.18, rect.Width * 0.34, rect.Height * 0.28), 1, 1);
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.54, rect.Y + rect.Height * 0.52, rect.Width * 0.3, rect.Height * 0.26), 1, 1);
    }

    private static void DrawPreset(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 2, 2);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.22, rect.Y + rect.Height * 0.32), new Point(rect.Right - rect.Width * 0.22, rect.Y + rect.Height * 0.32));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.22, rect.Center.Y), new Point(rect.Right - rect.Width * 0.36, rect.Center.Y));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.22, rect.Bottom - rect.Height * 0.32), new Point(rect.Right - rect.Width * 0.28, rect.Bottom - rect.Height * 0.32));
    }

    private static void DrawPackage(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.12, rect.Y + rect.Height * 0.24, rect.Width * 0.76, rect.Height * 0.58), 2, 2);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.12, rect.Y + rect.Height * 0.4), new Point(rect.Center.X, rect.Y + rect.Height * 0.2));
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.2), new Point(rect.Right - rect.Width * 0.12, rect.Y + rect.Height * 0.4));
    }

    private static void DrawEye(DrawingContext context, Rect rect, Pen pen, IBrush brush, bool crossed)
    {
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.1, rect.Center.Y), new Point(rect.Center.X, rect.Y + rect.Height * 0.28));
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.28), new Point(rect.Right - rect.Width * 0.1, rect.Center.Y));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.1, rect.Center.Y), new Point(rect.Center.X, rect.Bottom - rect.Height * 0.28));
        context.DrawLine(pen, new Point(rect.Center.X, rect.Bottom - rect.Height * 0.28), new Point(rect.X + rect.Width * 0.1, rect.Center.Y));
        context.DrawEllipse(brush, null, rect.Center, rect.Width * 0.08, rect.Height * 0.08);
        if (crossed)
        {
            context.DrawLine(pen, new Point(rect.X + rect.Width * 0.16, rect.Bottom - rect.Height * 0.12), new Point(rect.Right - rect.Width * 0.16, rect.Y + rect.Height * 0.12));
        }
    }

    private static void DrawLock(DrawingContext context, Rect rect, Pen pen, bool unlocked)
    {
        var body = new Rect(rect.X + rect.Width * 0.18, rect.Y + rect.Height * 0.45, rect.Width * 0.64, rect.Height * 0.4);
        context.DrawRectangle(null, pen, body, 2, 2);
        var shackleLeft = unlocked ? rect.X + rect.Width * 0.5 : rect.X + rect.Width * 0.32;
        context.DrawLine(pen, new Point(shackleLeft, rect.Y + rect.Height * 0.45), new Point(shackleLeft, rect.Y + rect.Height * 0.28));
        context.DrawLine(pen, new Point(shackleLeft, rect.Y + rect.Height * 0.28), new Point(rect.X + rect.Width * 0.68, rect.Y + rect.Height * 0.28));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.68, rect.Y + rect.Height * 0.28), new Point(rect.X + rect.Width * 0.68, rect.Y + rect.Height * 0.45));
    }

    private static void DrawPlus(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.22), new Point(rect.Center.X, rect.Bottom - rect.Height * 0.22));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.22, rect.Center.Y), new Point(rect.Right - rect.Width * 0.22, rect.Center.Y));
    }

    private static void DrawSave(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 2, 2);
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.24, rect.Y + rect.Height * 0.14, rect.Width * 0.44, rect.Height * 0.24), 1, 1);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.28, rect.Bottom - rect.Height * 0.22), new Point(rect.Right - rect.Width * 0.28, rect.Bottom - rect.Height * 0.22));
    }

    private static void DrawOpen(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.14, rect.Y + rect.Height * 0.36), new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.36));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.36), new Point(rect.X + rect.Width * 0.52, rect.Y + rect.Height * 0.48));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.52, rect.Y + rect.Height * 0.48), new Point(rect.Right - rect.Width * 0.12, rect.Y + rect.Height * 0.48));
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.14, rect.Y + rect.Height * 0.42, rect.Width * 0.72, rect.Height * 0.42), 2, 2);
    }

    private static void DrawSettings(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawEllipse(null, pen, rect.Center, rect.Width * 0.28, rect.Height * 0.28);
        context.DrawEllipse(null, pen, rect.Center, rect.Width * 0.08, rect.Height * 0.08);
        DrawPlus(context, rect, pen);
    }

    private static void DrawWarning(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.12), new Point(rect.Right - rect.Width * 0.1, rect.Bottom - rect.Height * 0.14));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.1, rect.Bottom - rect.Height * 0.14), new Point(rect.X + rect.Width * 0.1, rect.Bottom - rect.Height * 0.14));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.1, rect.Bottom - rect.Height * 0.14), new Point(rect.Center.X, rect.Y + rect.Height * 0.12));
        context.DrawLine(pen, new Point(rect.Center.X, rect.Y + rect.Height * 0.38), new Point(rect.Center.X, rect.Y + rect.Height * 0.62));
    }

    private static void DrawError(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawEllipse(null, pen, rect.Center, rect.Width * 0.38, rect.Height * 0.38);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.34, rect.Y + rect.Height * 0.34), new Point(rect.Right - rect.Width * 0.34, rect.Bottom - rect.Height * 0.34));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.34, rect.Y + rect.Height * 0.34), new Point(rect.X + rect.Width * 0.34, rect.Bottom - rect.Height * 0.34));
    }

    private static void DrawSuccess(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.18, rect.Center.Y), new Point(rect.X + rect.Width * 0.42, rect.Bottom - rect.Height * 0.22));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.42, rect.Bottom - rect.Height * 0.22), new Point(rect.Right - rect.Width * 0.12, rect.Y + rect.Height * 0.2));
    }

    private static void DrawDrag(DrawingContext context, Rect rect, Pen pen)
    {
        for (var y = 0.3; y <= 0.7; y += 0.2)
        {
            context.DrawLine(pen, new Point(rect.X + rect.Width * 0.28, rect.Y + rect.Height * y), new Point(rect.Right - rect.Width * 0.28, rect.Y + rect.Height * y));
        }
    }

    private static void DrawEffect(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(rect.X + rect.Width * 0.32, rect.Y + rect.Height * 0.36), rect.Width * 0.16, rect.Height * 0.16);
        context.DrawEllipse(null, pen, new Point(rect.X + rect.Width * 0.68, rect.Y + rect.Height * 0.64), rect.Width * 0.16, rect.Height * 0.16);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.46), new Point(rect.X + rect.Width * 0.58, rect.Y + rect.Height * 0.54));
    }

    private static void DrawAudio(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.12, rect.Y + rect.Height * 0.42, rect.Width * 0.22, rect.Height * 0.2), 1, 1);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.34, rect.Y + rect.Height * 0.42), new Point(rect.X + rect.Width * 0.62, rect.Y + rect.Height * 0.24));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.62, rect.Y + rect.Height * 0.24), new Point(rect.X + rect.Width * 0.62, rect.Bottom - rect.Height * 0.24));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.62, rect.Bottom - rect.Height * 0.24), new Point(rect.X + rect.Width * 0.34, rect.Y + rect.Height * 0.62));
        context.DrawArc(pen, rect.Deflate(rect.Width * 0.1), 320, 80);
    }

    private static void DrawSearch(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(rect.X + rect.Width * 0.42, rect.Y + rect.Height * 0.42), rect.Width * 0.24, rect.Height * 0.24);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.58, rect.Y + rect.Height * 0.58), new Point(rect.Right - rect.Width * 0.1, rect.Bottom - rect.Height * 0.1));
    }

    private static void DrawMenu(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.2, rect.Y + rect.Height * 0.3), new Point(rect.Right - rect.Width * 0.2, rect.Y + rect.Height * 0.3));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.2, rect.Center.Y), new Point(rect.Right - rect.Width * 0.2, rect.Center.Y));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.2, rect.Bottom - rect.Height * 0.3), new Point(rect.Right - rect.Width * 0.2, rect.Bottom - rect.Height * 0.3));
    }

    private static void DrawGrid(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 1, 1);
        context.DrawLine(pen, new Point(rect.X + rect.Width / 3, rect.Y), new Point(rect.X + rect.Width / 3, rect.Bottom));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 2 / 3, rect.Y), new Point(rect.X + rect.Width * 2 / 3, rect.Bottom));
        context.DrawLine(pen, new Point(rect.X, rect.Y + rect.Height / 3), new Point(rect.Right, rect.Y + rect.Height / 3));
        context.DrawLine(pen, new Point(rect.X, rect.Y + rect.Height * 2 / 3), new Point(rect.Right, rect.Y + rect.Height * 2 / 3));
    }

    private static void DrawSafeArea(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 1, 1);
        context.DrawRectangle(null, pen, rect.Deflate(rect.Width * 0.16), 1, 1);
    }

    private static void DrawFit(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, rect, 1, 1);
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.18, rect.Y + rect.Height * 0.18), new Point(rect.X + rect.Width * 0.36, rect.Y + rect.Height * 0.18));
        context.DrawLine(pen, new Point(rect.X + rect.Width * 0.18, rect.Y + rect.Height * 0.18), new Point(rect.X + rect.Width * 0.18, rect.Y + rect.Height * 0.36));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.18, rect.Bottom - rect.Height * 0.18), new Point(rect.Right - rect.Width * 0.36, rect.Bottom - rect.Height * 0.18));
        context.DrawLine(pen, new Point(rect.Right - rect.Width * 0.18, rect.Bottom - rect.Height * 0.18), new Point(rect.Right - rect.Width * 0.18, rect.Bottom - rect.Height * 0.36));
    }

    private static void DrawLayer(DrawingContext context, Rect rect, Pen pen)
    {
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.2, rect.Y + rect.Height * 0.14, rect.Width * 0.58, rect.Height * 0.36), 2, 2);
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.12, rect.Y + rect.Height * 0.34, rect.Width * 0.58, rect.Height * 0.36), 2, 2);
        context.DrawRectangle(null, pen, new Rect(rect.X + rect.Width * 0.28, rect.Y + rect.Height * 0.52, rect.Width * 0.58, rect.Height * 0.36), 2, 2);
    }
}

internal static class StudioIconDrawingExtensions
{
    public static void DrawArc(this DrawingContext context, Pen pen, Rect rect, double startAngle, double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            var start = PointOnEllipse(rect, startAngle);
            var end = PointOnEllipse(rect, startAngle + sweepAngle);
            stream.BeginFigure(start, false);
            stream.ArcTo(end, new Size(rect.Width / 2, rect.Height / 2), 0, sweepAngle > 180, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnEllipse(Rect rect, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(
            rect.Center.X + Math.Cos(radians) * rect.Width / 2,
            rect.Center.Y + Math.Sin(radians) * rect.Height / 2);
    }
}
