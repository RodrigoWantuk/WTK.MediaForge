using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

public sealed record ScenePublishedState
{
    public required CanvasId CanvasId { get; init; }

    public required SceneVersionId VersionId { get; init; }

    public required long Revision { get; init; }
}
