using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scene;

internal interface ISceneRuntimeObserver
{
    void OnSceneDirtyRegionChanged(SceneDirtyRegion region);

    void OnHiddenLayerSkipped(DrawObjectId layerId);
}
