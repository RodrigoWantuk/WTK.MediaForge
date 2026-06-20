using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderBackend : IDisposable
{
    void BindOutput(RenderOutputBindingSnapshot binding);

    void UnbindOutput(RenderOutputId outputId);

    void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize);

    IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot);

    ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
