using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeFrameDroppedEventArgs : EventArgs
{
    public MediaForgeFrameDroppedEventArgs(MediaForgeDiagnostic diagnostic) =>
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));

    public MediaForgeDiagnostic Diagnostic { get; }

    public string ReasonCode => Diagnostic.Code;

    public string Message => Diagnostic.Message;
}
