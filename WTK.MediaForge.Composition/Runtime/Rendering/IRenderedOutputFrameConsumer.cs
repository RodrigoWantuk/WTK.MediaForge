namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderedOutputFrameConsumer
{
    void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch);
}
