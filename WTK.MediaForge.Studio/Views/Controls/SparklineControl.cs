using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WTK.MediaForge.Studio.Views.Controls;

public sealed class SparklineControl : Control
{
    private static readonly double[] Samples = { 0.62, 0.58, 0.64, 0.49, 0.42, 0.47, 0.36, 0.44, 0.31, 0.29, 0.34, 0.28 };

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<SparklineControl, IBrush>(nameof(Stroke), Brushes.Cyan);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SparklineControl, double>(nameof(StrokeThickness), 2d);

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var pen = new Pen(Stroke, StrokeThickness);
        Point? previous = null;

        for (var index = 0; index < Samples.Length; index++)
        {
            var x = Samples.Length == 1 ? 0 : Bounds.Width * index / (Samples.Length - 1);
            var y = Bounds.Height * Samples[index];
            var current = new Point(x, y);

            if (previous is not null)
            {
                context.DrawLine(pen, previous.Value, current);
            }

            previous = current;
        }
    }
}
