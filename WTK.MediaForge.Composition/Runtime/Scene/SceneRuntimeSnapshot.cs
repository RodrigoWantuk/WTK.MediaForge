using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Scenes.Editing;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal sealed class SceneRuntimeSnapshot
{
    public required ProjectStateSnapshot ProjectState { get; init; }

    public IReadOnlyDictionary<DrawObjectId, SceneLayerRuntimeState> Layers { get; init; } =
        new Dictionary<DrawObjectId, SceneLayerRuntimeState>();

    public SceneDirtyRegion DirtyRegion { get; init; } = SceneDirtyRegion.Full;

    public long Version { get; init; }

    public MediaForgeRenderGraphPlan? CachedRenderGraphPlan { get; init; }

    public IReadOnlySet<DrawObjectId> HiddenLayerIds { get; init; } = new HashSet<DrawObjectId>();

    public SceneVersionBinding VersionBinding { get; init; } = SceneVersionBinding.Published;
}
