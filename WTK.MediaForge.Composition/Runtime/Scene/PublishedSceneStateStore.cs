using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class PublishedSceneStateStore
{
    private readonly SceneVersionIndex _versionIndex = new();

    public ProjectStateSnapshot? ProjectState { get; private set; }

    public IReadOnlyDictionary<CanvasId, ScenePublishedState> States => _versionIndex.PublishedStates;

    public void Sync(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        ProjectState = projectState;
        _versionIndex.Sync(projectState);
    }

    public SceneVersionId GetVersion(CanvasId canvasId) => _versionIndex.GetPublishedVersion(canvasId);

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CreateVersionMap() =>
        _versionIndex.CreateVersionMap();

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CreateVersionSnapshotMap() =>
        _versionIndex.CreateVersionSnapshotMap();
}
