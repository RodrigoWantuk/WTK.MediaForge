namespace WTK.MediaForge.Diagnostics;

public sealed class NullDiagnosticsSink : IMediaForgeDiagnosticsSink
{
    public static NullDiagnosticsSink Instance { get; } = new();

    private NullDiagnosticsSink() { }

    public void Report(MediaForgeDiagnostic diagnostic) { }
}
