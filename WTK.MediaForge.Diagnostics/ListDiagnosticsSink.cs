namespace WTK.MediaForge.Diagnostics;

public sealed class ListDiagnosticsSink : IMediaForgeDiagnosticsSink
{
    private readonly List<MediaForgeDiagnostic> _diagnostics = [];

    public IReadOnlyList<MediaForgeDiagnostic> Diagnostics => _diagnostics;

    public void Report(MediaForgeDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

    public void Clear() => _diagnostics.Clear();
}
