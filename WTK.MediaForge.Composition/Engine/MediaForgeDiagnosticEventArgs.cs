using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Engine;

public sealed class MediaForgeDiagnosticEventArgs : EventArgs
{
    public MediaForgeDiagnosticEventArgs(MediaForgeDiagnostic diagnostic) =>
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));

    public MediaForgeDiagnostic Diagnostic { get; }
}
