using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Graphics.Vulkan;

public sealed class MediaForgeVulkanRenderBackendFactory
{
    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(threadGuard);

        if (!MediaForgeVulkanRenderer.TryCreate(
                threadGuard,
                diagnostics,
                NullVulkanRendererFaultInjector.Instance,
                out var renderer))
        {
            backend = null;
            return false;
        }

        backend = renderer;
        return true;
    }
}
