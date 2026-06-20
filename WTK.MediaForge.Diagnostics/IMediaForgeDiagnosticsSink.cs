namespace WTK.MediaForge.Diagnostics;

public interface IMediaForgeDiagnosticsSink
{
    void Report(MediaForgeDiagnostic diagnostic);
}
