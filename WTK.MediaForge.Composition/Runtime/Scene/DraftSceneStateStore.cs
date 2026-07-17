using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class DraftSceneStateStore
{
    private readonly Dictionary<SceneEditSessionId, DraftEntry> _drafts = [];

    public bool TryGet(SceneEditSessionId sessionId, out SceneDraftState? state)
    {
        if (_drafts.TryGetValue(sessionId, out var entry))
        {
            state = entry.State;
            return true;
        }

        state = null;
        return false;
    }

    public bool TryGetProjectState(SceneEditSessionId sessionId, out ProjectStateSnapshot? projectState)
    {
        if (_drafts.TryGetValue(sessionId, out var entry))
        {
            projectState = entry.ProjectState;
            return true;
        }

        projectState = null;
        return false;
    }

    public SceneDraftState Upsert(
        SceneEditSessionId sessionId,
        ProjectStateSnapshot projectState,
        SceneDraftState state)
    {
        ArgumentNullException.ThrowIfNull(projectState);
        ArgumentNullException.ThrowIfNull(state);

        _drafts[sessionId] = new DraftEntry(projectState, state);
        return state;
    }

    public bool Remove(SceneEditSessionId sessionId) => _drafts.Remove(sessionId);

    public void Clear() => _drafts.Clear();

    private sealed record DraftEntry(ProjectStateSnapshot ProjectState, SceneDraftState State);
}
