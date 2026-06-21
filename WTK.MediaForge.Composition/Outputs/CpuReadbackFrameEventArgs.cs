namespace WTK.MediaForge.Composition.Outputs;

public sealed class CpuReadbackFrameEventArgs : EventArgs
{
    internal CpuReadbackFrameEventArgs(RenderOutputFrameInfo frame) =>
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));

    public RenderOutputFrameInfo Frame { get; }
}
