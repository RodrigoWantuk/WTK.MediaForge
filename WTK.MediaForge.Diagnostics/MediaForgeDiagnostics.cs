namespace WTK.MediaForge.Diagnostics;

public static class MediaForgeDiagnostics
{
    public static IMediaForgeDiagnosticsSink? Current { get; set; }

    public static void Report(MediaForgeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Current?.Report(diagnostic);
    }
}
