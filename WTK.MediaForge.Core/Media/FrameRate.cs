namespace WTK.MediaForge.Core.Media;

public readonly record struct FrameRate(uint Numerator, uint Denominator)
{
    public double FramesPerSecond =>
        Denominator == 0 ? 0 : (double)Numerator / Denominator;

    public override string ToString() => $"{Numerator}/{Denominator}";
}
