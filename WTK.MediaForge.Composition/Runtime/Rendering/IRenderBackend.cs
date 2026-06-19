using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public interface IRenderBackend
{
    void BindOutput(RenderOutputBindingSnapshot binding);

    void UnbindOutput(RenderOutputId outputId);

    void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize);

    void Render(RenderFrameSnapshot snapshot);
}
