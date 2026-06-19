using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

public enum SnapshotDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Fatal = 3
}

public enum SnapshotDiagnosticKind
{
    SourceNotRegistered = 0,
    SourceNoFrame = 1,
    NestedCanvasMissing = 2,
    NestedCanvasDepthExceeded = 3
}

public sealed class SnapshotDiagnostic
{
    public SnapshotDiagnosticKind Kind { get; init; }

    public SnapshotDiagnosticSeverity Severity { get; init; }

    public string Message { get; init; } = string.Empty;

    public SourceId? SourceId { get; init; }

    public CanvasId? CanvasId { get; init; }

    public DrawObjectId? DrawObjectId { get; init; }

    public static SnapshotDiagnosticSeverity DefaultSeverity(SnapshotDiagnosticKind kind) =>
        kind switch
        {
            SnapshotDiagnosticKind.SourceNoFrame => SnapshotDiagnosticSeverity.Warning,
            SnapshotDiagnosticKind.SourceNotRegistered => SnapshotDiagnosticSeverity.Error,
            SnapshotDiagnosticKind.NestedCanvasMissing => SnapshotDiagnosticSeverity.Error,
            SnapshotDiagnosticKind.NestedCanvasDepthExceeded => SnapshotDiagnosticSeverity.Error,
            _ => SnapshotDiagnosticSeverity.Error
        };
}
