using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WTK.MediaForge.Studio.Views.Controls;

public sealed class CheckerboardControl : Control
{
    public static readonly StyledProperty<double> CellSizeProperty =
        AvaloniaProperty.Register<CheckerboardControl, double>(nameof(CellSize), 18d);

    public static readonly StyledProperty<IBrush> BrushAProperty =
        AvaloniaProperty.Register<CheckerboardControl, IBrush>(nameof(BrushA), Brushes.Transparent);

    public static readonly StyledProperty<IBrush> BrushBProperty =
        AvaloniaProperty.Register<CheckerboardControl, IBrush>(nameof(BrushB), Brushes.Black);

    public double CellSize
    {
        get => GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    public IBrush BrushA
    {
        get => GetValue(BrushAProperty);
        set => SetValue(BrushAProperty, value);
    }

    public IBrush BrushB
    {
        get => GetValue(BrushBProperty);
        set => SetValue(BrushBProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var cell = Math.Max(4d, CellSize);
        var rows = (int)Math.Ceiling(Bounds.Height / cell);
        var columns = (int)Math.Ceiling(Bounds.Width / cell);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var brush = (row + column) % 2 == 0 ? BrushA : BrushB;
                context.FillRectangle(brush, new Rect(column * cell, row * cell, cell, cell));
            }
        }
    }
}
