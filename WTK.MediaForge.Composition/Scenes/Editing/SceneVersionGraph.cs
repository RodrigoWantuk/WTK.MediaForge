using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Scenes.Editing;

internal sealed class SceneVersionGraph
{
    private readonly IReadOnlyDictionary<CanvasId, SceneVersionId> _canvasVersions;

    public SceneVersionGraph(CanvasId rootCanvasId, IReadOnlyDictionary<CanvasId, SceneVersionId> canvasVersions)
    {
        RootCanvasId = rootCanvasId;
        _canvasVersions = canvasVersions.ToDictionary(static pair => pair.Key, static pair => pair.Value);
    }

    public CanvasId RootCanvasId { get; }

    public IReadOnlyDictionary<CanvasId, SceneVersionId> CanvasVersions => _canvasVersions;

    public bool TryGetVersion(CanvasId canvasId, out SceneVersionId versionId) =>
        _canvasVersions.TryGetValue(canvasId, out versionId);
}
