namespace WTK.MediaForge.Composition.Outputs;

public sealed class FrameNotificationEventArgs : EventArgs
{
    internal FrameNotificationEventArgs(RenderOutputFrameInfo frame) =>
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));

    public RenderOutputFrameInfo Frame { get; }
}
