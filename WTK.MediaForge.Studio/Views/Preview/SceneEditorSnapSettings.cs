using Avalonia;
using Avalonia.Input;

namespace WTK.MediaForge.Studio.Views.Preview;

public sealed class SceneEditorSnapSettings
{
    public double MinorGridSize { get; init; } = 10;

    public double MajorGridSize { get; init; } = 100;

    public double DefaultSnapSize { get; init; } = 10;

    public double PrecisionSnapSize { get; init; } = 1;

    public double ArrowNudgeSize { get; init; } = 1;

    public double LargeArrowNudgeSize { get; init; } = 10;

    public double HugeArrowNudgeSize { get; init; } = 50;

    public double GetMoveSnap(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            return 0;
        }

        return modifiers.HasFlag(KeyModifiers.Control) ? PrecisionSnapSize : DefaultSnapSize;
    }

    public double GetResizeSnap(KeyModifiers modifiers)
    {
        return modifiers.HasFlag(KeyModifiers.Control) ? PrecisionSnapSize : DefaultSnapSize;
    }

    public double GetNudgeSize(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control) && modifiers.HasFlag(KeyModifiers.Shift))
        {
            return HugeArrowNudgeSize;
        }

        return modifiers.HasFlag(KeyModifiers.Shift) ? LargeArrowNudgeSize : ArrowNudgeSize;
    }

    public static double Snap(double value, double step)
    {
        if (step <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return value;
        }

        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    public static Point Snap(Point value, double step)
    {
        return new Point(Snap(value.X, step), Snap(value.Y, step));
    }
}
