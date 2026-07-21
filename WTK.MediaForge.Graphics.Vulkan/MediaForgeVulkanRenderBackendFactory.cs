using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using WTK.MediaForge.Graphics.Vulkan.Text;

namespace WTK.MediaForge.Graphics.Vulkan;

internal sealed class MediaForgeVulkanRenderBackendFactory : IRenderBackendFactory
{
    private readonly IFontAtlasRasterizer? _fontAtlasRasterizer;
    private readonly GpuAdapterAffinityState? _adapterAffinity;

    public MediaForgeVulkanRenderBackendFactory(
        IFontAtlasRasterizer? fontAtlasRasterizer = null,
        GpuAdapterAffinityState? adapterAffinity = null)
    {
        _fontAtlasRasterizer = fontAtlasRasterizer;
        _adapterAffinity = adapterAffinity;
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

        if (OperatingSystem.IsWindows() && _adapterAffinity is not null)
        {
            if (!renderer!.DeviceLuidValid)
            {
                renderer.Dispose();
                backend = null;
                return false;
            }

            _adapterAffinity.Publish(renderer.DeviceLuid, renderer.DeviceName);
        }

        backend = _adapterAffinity is null
            ? renderer
            : new AdapterAffinityRenderBackend(renderer!, _adapterAffinity);
        return true;
    }

    private sealed class AdapterAffinityRenderBackend(
        IRenderBackend inner,
        GpuAdapterAffinityState adapterAffinity) : IRenderBackend, IRenderBackendResourceDiagnostics
    {
        private int _disposed;

        public void BindOutput(RenderOutputBindingSnapshot binding) => inner.BindOutput(binding);

        public void UnbindOutput(RenderOutputId outputId) => inner.UnbindOutput(outputId);

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) =>
            inner.ResizeOutput(outputId, surfaceSize);

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) => inner.Submit(snapshot);

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            inner.WaitIdleAsync(timeout, cancellationToken);

        public RenderBackendResourceSnapshot GetResourceSnapshot() =>
            inner is IRenderBackendResourceDiagnostics diagnostics
                ? diagnostics.GetResourceSnapshot()
                : new RenderBackendResourceSnapshot();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                inner.Dispose();
            }
            finally
            {
                adapterAffinity.Invalidate();
            }
        }
    }
}
