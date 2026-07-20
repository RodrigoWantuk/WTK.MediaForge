namespace WTK.MediaForge.Diagnostics;

public static class MediaForgeDiagnostics
{
    public static IMediaForgeDiagnosticsSink? Current { get; set; }

    public static void Report(MediaForgeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        Current?.Report(diagnostic);
    }

    public static void Report(IMediaForgeDiagnosticsSink? sink, MediaForgeDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (sink is not null)
            sink.Report(diagnostic);
        else
            Report(diagnostic);
    }

    public static void Report(
        IMediaForgeDiagnosticsSink? sink,
        MediaForgeDiagnosticSeverity severity,
        string code,
        string message,
        string component,
        Exception? exception = null,
        Guid? sourceId = null,
        string? sourceName = null,
        long? frameNumber = null,
        int? slotIndex = null,
        Guid? outputId = null)
    {
        Report(
            sink,
            MediaForgeDiagnosticFactory.Create(
                severity,
                code,
                message,
                sourceId,
                sourceName,
                frameNumber,
                slotIndex,
                component,
                exception,
                outputId));
    }
}
