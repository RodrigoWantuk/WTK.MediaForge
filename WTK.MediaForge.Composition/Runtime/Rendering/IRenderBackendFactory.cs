using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderBackendFactory
{
    bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend);
}
