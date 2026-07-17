using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

public sealed record SceneDraftState
{
    public required SceneEditSessionId SessionId { get; init; }

    public required CanvasId CanvasId { get; init; }

    public required SceneVersionId BasePublishedVersionId { get; init; }

    public required SceneVersionId DraftVersionId { get; init; }

    public required bool HasChanges { get; init; }
}
