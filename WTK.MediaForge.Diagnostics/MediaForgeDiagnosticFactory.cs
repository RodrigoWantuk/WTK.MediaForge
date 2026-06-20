namespace WTK.MediaForge.Diagnostics;

public static class MediaForgeDiagnosticFactory
{
    public static MediaForgeDiagnostic Create(
        MediaForgeDiagnosticSeverity severity,
        string code,
        string message,
        Guid? sourceId = null,
        string? sourceName = null,
        long? frameNumber = null,
        int? slotIndex = null,
        string? component = null,
        Exception? exception = null) =>
        new(
            severity,
            code,
            message,
            DateTimeOffset.UtcNow,
            sourceId,
            sourceName,
            frameNumber,
            slotIndex,
            component,
            exception);
}
