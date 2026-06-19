namespace WTK.MediaForge.Core.Capture;

public readonly struct CaptureCenterPixel
{
    public byte Blue { get; init; }
    public byte Green { get; init; }
    public byte Red { get; init; }
    public byte Alpha { get; init; }

    public int Luminance => (Red * 299 + Green * 587 + Blue * 114) / 1000;

    public bool IsLikelyEmpty => Luminance < 4 && Alpha < 4;

    public override string ToString() => $"BGRA=({Blue},{Green},{Red},{Alpha}) lum={Luminance}";
}
