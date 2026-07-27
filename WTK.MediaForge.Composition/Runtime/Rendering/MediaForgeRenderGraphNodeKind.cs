namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum MediaForgeRenderGraphNodeKind
{
    SourceFrame = 0,
    SourceEffectChain = 1,
    LayerEffectChain = 2,
    SourceLayer = 3,
    PrimitiveLayer = 4,
    CanvasRender = 5,
    CanvasEffectChain = 6,
    AdjustmentLayerCheckpoint = 7,
    OutputTransition = 8,
    OutputPass = 9,
    CanvasLayer = 10
}
