namespace WTK.MediaForge.Core.Capture;

public readonly struct DesktopRect
{
    public DesktopRect(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }

    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}
