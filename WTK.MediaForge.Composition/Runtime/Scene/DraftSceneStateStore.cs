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
        SceneDraftState state,
        IDisposable versionPin)
    {
        ArgumentNullException.ThrowIfNull(projectState);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(versionPin);

        if (_drafts.Remove(sessionId, out var previous))
            previous.VersionPin.Dispose();

        _drafts[sessionId] = new DraftEntry(projectState, state, versionPin);
        return state;
    }

    public bool Remove(SceneEditSessionId sessionId)
    {
        if (!_drafts.Remove(sessionId, out var entry))
            return false;

        entry.VersionPin.Dispose();
        return true;
    }

    public void Clear()
    {
        foreach (var entry in _drafts.Values)
            entry.VersionPin.Dispose();
        _drafts.Clear();
    }

    private sealed record DraftEntry(
        ProjectStateSnapshot ProjectState,
        SceneDraftState State,
        IDisposable VersionPin);
}
