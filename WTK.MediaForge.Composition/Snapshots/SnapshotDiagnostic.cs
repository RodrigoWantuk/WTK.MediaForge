using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Snapshots;

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

    public string Message { get; init; } = string.Empty;

    public SourceId? SourceId { get; init; }

    public CanvasId? CanvasId { get; init; }

    public DrawObjectId? DrawObjectId { get; init; }
}
