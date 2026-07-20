namespace WTK.MediaForge.Diagnostics;

public sealed record MediaForgeDiagnostic(
    MediaForgeDiagnosticSeverity Severity,
    string Code,
    string Message,
    DateTimeOffset Timestamp,
    Guid? SourceId = null,
    string? SourceName = null,
    long? FrameNumber = null,
    int? SlotIndex = null,
    string? Component = null,
    Exception? Exception = null,
    Guid? OutputId = null);
