using WTK.MediaForge.Composition.Scenes.Editing;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed record SceneVersionPinGraph(
    IReadOnlySet<SceneVersionId> DirectVersionIds,
    IReadOnlySet<SceneVersionId> TransitiveVersionIds)
{
    public static SceneVersionPinGraph Empty { get; } =
        new(new HashSet<SceneVersionId>(), new HashSet<SceneVersionId>());

    public bool Contains(SceneVersionId versionId) =>
        DirectVersionIds.Contains(versionId) || TransitiveVersionIds.Contains(versionId);
}
