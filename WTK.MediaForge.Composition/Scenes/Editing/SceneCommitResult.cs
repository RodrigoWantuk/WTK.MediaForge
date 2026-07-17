using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

public sealed record SceneCommitResult
{
    public required SceneEditSessionId SessionId { get; init; }

    public required CanvasId CanvasId { get; init; }

    public required SceneVersionId OldVersionId { get; init; }

    public required SceneVersionId NewVersionId { get; init; }

    public IReadOnlyList<CanvasId> AffectedCanvases { get; init; } = Array.Empty<CanvasId>();

    public IReadOnlyList<RenderOutputId> AffectedOutputs { get; init; } = Array.Empty<RenderOutputId>();

    public required bool TransitionRequested { get; init; }
}
