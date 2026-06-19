namespace WTK.MediaForge.Core.Frames;

public readonly record struct FrameSize(uint Width, uint Height)
{
    public bool IsEmpty => Width == 0 || Height == 0;

    public override string ToString()
    {
        return $"{Width}x{Height}";
    }
}