using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

public sealed record SceneEditSessionDescriptor
{
    public required SceneEditSessionId SessionId { get; init; }

    public required CanvasId CanvasId { get; init; }

    public required SceneEditMode Mode { get; init; }

    public required SceneVersionId BasePublishedVersionId { get; init; }

    public SceneVersionId? DraftVersionId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
