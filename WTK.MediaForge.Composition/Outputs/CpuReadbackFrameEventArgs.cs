namespace WTK.MediaForge.Composition.Outputs;

public sealed class CpuReadbackFrameEventArgs : EventArgs
{
    internal CpuReadbackFrameEventArgs(CpuReadbackFrame frame) =>
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));

    public CpuReadbackFrame Frame { get; }
}
