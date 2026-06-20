namespace WTK.MediaForge.Diagnostics;

public sealed class InMemoryDiagnosticsSink : IMediaForgeDiagnosticsSink
{
    private readonly List<MediaForgeDiagnostic> _diagnostics = [];
    private readonly object _gate = new();

    public IReadOnlyList<MediaForgeDiagnostic> Diagnostics
    {
        get
        {
            lock (_gate)
                return _diagnostics.ToArray();
        }
    }

    public void Report(MediaForgeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        lock (_gate)
            _diagnostics.Add(diagnostic);
    }

    public void Clear()
    {
        lock (_gate)
            _diagnostics.Clear();
    }
}
