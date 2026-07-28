namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderedOutputFrameConsumer
{
    void PublishCompletedFrames(RenderedOutputFrameBatch frameBatch);

    /// <summary>
    /// Gives consumers that participate in physical encoded dispatch the exact output ids that
    /// may enter hardware encoding. The default preserves generic preview/debug consumers.
    /// A null set means the submission did not carry a physical dispatch plan.
    /// </summary>
    void PublishCompletedFrames(
        RenderedOutputFrameBatch frameBatch,
        IReadOnlySet<WTK.MediaForge.Core.Identifiers.RenderOutputId>? encodedOutputDispatchIds) =>
        PublishCompletedFrames(frameBatch);
}
