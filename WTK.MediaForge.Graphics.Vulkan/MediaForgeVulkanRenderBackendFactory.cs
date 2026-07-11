using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Graphics.Vulkan.Text;

namespace WTK.MediaForge.Graphics.Vulkan;

internal sealed class MediaForgeVulkanRenderBackendFactory : IRenderBackendFactory
{
    private readonly IFontAtlasRasterizer? _fontAtlasRasterizer;

    public MediaForgeVulkanRenderBackendFactory(IFontAtlasRasterizer? fontAtlasRasterizer = null)
    {
        _fontAtlasRasterizer = fontAtlasRasterizer;
    }

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
                _fontAtlasRasterizer,
                out var renderer))
        {
            backend = null;
            return false;
        }

        backend = renderer;
        return true;
    }
}
