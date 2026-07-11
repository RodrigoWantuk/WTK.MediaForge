using Vortice.Direct3D11;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Scheduling;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Windows.Media.Interop;

internal sealed class WindowsRenderedOutputEncoderSurfaceExporter : IRenderedOutputEncoderSurfaceExporter
{
    private readonly ID3D11Device _device;

    public WindowsRenderedOutputEncoderSurfaceExporter(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public bool CanExport(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(requirement);

        return requirement.RequiresGpuSurface &&
               surface.BackendKind == RenderBackendKind.Vulkan &&
               surface.BackendSurface is VulkanOffscreenRenderTarget &&
               surface.Size.Width == requirement.Width &&
               surface.Size.Height == requirement.Height &&
               string.Equals(ToD3D11SharedTextureFormat(surface.Format), requirement.PixelFormat, StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<HardwareEncoderInputLease> ExportAsync(
        IRenderedOutputSurfaceLease surface,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanExport(surface, requirement))
        {
            throw new NotSupportedException(
                $"Rendered output surface cannot be exported as {requirement.PixelFormat} for hardware encoding without a GPU-only path.");
        }

        var target = (VulkanOffscreenRenderTarget)surface.BackendSurface!;
        var descriptor = new GpuVideoFrameDescriptor
        {
            Width = checked((int)surface.Size.Width),
            Height = checked((int)surface.Size.Height),
            Format = ToD3D11SharedTextureFormat(surface.Format),
            TransportKind = MediaTransportKind.GpuSurface
        };

        using var exporter = new VulkanToD3D11EncoderSurfaceExporter(target.DeviceContext, _device);
        return exporter.ExportForEncoderAsync(descriptor, target, auditSink, cancellationToken);
    }

    private static string ToD3D11SharedTextureFormat(RenderPixelFormat format) =>
        format switch
        {
            RenderPixelFormat.Rgba8Unorm => "B8G8R8A8_UNORM",
            _ => format.ToString()
        };
}
