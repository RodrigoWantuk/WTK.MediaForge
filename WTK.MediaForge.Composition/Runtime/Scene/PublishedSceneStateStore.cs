using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class PublishedSceneStateStore
{
    private readonly SceneVersionStore _versionStore = new();

    public ProjectStateSnapshot? ProjectState { get; private set; }

    public IReadOnlyDictionary<CanvasId, ScenePublishedState> States => _versionStore.PublishedStates;

    public SceneVersionRetentionSnapshot RetentionSnapshot => _versionStore.GetRetentionSnapshot();

    public void Sync(ProjectStateSnapshot projectState)
    {
        ArgumentNullException.ThrowIfNull(projectState);

        ProjectState = projectState;
        _versionStore.Sync(projectState);
    }

    public SceneVersionId GetVersion(CanvasId canvasId) => _versionStore.GetPublishedVersion(canvasId);

    public IDisposable RegisterAndPinVersion(CanvasStateSnapshot canvas, SceneVersionId versionId, string owner) =>
        _versionStore.RegisterAndPinVersion(canvas, versionId, owner);

    public IDisposable PinVersions(IEnumerable<SceneVersionId> versionIds, string owner) =>
        _versionStore.PinVersions(versionIds, owner);

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CreateVersionMap() =>
        _versionStore.CreateVersionMap();

    public IReadOnlyDictionary<SceneVersionId, CanvasStateSnapshot> CreateVersionSnapshotMap() =>
        _versionStore.CreateVersionSnapshotMap();
}
