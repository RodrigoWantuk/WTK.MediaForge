using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderedOutputSurfaceLease : IAsyncDisposable
{
    RenderOutputId OutputId { get; }

    FrameSize Size { get; }

    RenderPixelFormat Format { get; }

    RenderBackendKind BackendKind { get; }

    object? BackendSurface { get; }
}
