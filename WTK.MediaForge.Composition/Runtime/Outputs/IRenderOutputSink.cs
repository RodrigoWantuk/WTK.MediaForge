using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Outputs;

public interface IRenderOutputSink : IAsyncDisposable
{
    RenderOutputBindingSnapshot CreateBinding(
        RenderOutputId outputId,
        FrameSize surfaceSize,
        long bindingVersion);
}
