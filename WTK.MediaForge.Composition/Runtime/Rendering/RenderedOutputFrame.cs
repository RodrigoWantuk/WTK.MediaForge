using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderedOutputFrame
{
    public RenderedOutputFrame(
        RenderOutputId outputId,
        FrameSize size,
        RenderPixelFormat format,
        RenderBackendKind backendKind)
    {
        OutputId = outputId;
        Size = size;
        Format = format;
        BackendKind = backendKind;
    }

    public RenderOutputId OutputId { get; }

    public FrameSize Size { get; }

    public RenderPixelFormat Format { get; }

    public RenderBackendKind BackendKind { get; }
}
