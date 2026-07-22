namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum MediaForgeRenderGraphNodeKind
{
    SourceFrame = 0,
    SourceEffectChain = 1,
    LayerEffectChain = 2,
    PrimitiveLayer = 3,
    CanvasRender = 4,
    OutputTransition = 5,
    OutputPass = 6
}
