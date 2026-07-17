namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal enum MediaForgeRenderGraphNodeKind
{
    SourceFrame = 0,
    SourceEffectChain = 1,
    PrimitiveLayer = 2,
    CanvasRender = 3,
    OutputTransition = 4,
    OutputPass = 5
}
