using Vortice.Direct3D11;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan.Rendering;

namespace WTK.MediaForge.Windows.Media.Interop;

public sealed class VulkanToD3D11EncoderSurfaceExporter : IGpuFrameExporter, IDisposable
{
    private readonly VulkanHeadlessDevice? _vulkanDevice;
    private readonly ID3D11Device _device;
    private bool _disposed;

    public VulkanToD3D11EncoderSurfaceExporter(ID3D11Device device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    internal VulkanToD3D11EncoderSurfaceExporter(VulkanHeadlessDevice vulkanDevice, ID3D11Device device)
    {
        _vulkanDevice = vulkanDevice ?? throw new ArgumentNullException(nameof(vulkanDevice));
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
        requirement.RequiresGpuSurface
        && descriptor.Width == requirement.Width
        && descriptor.Height == requirement.Height
        && !requirement.PixelFormat.Contains("cpu", StringComparison.OrdinalIgnoreCase);

    public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
        GpuVideoFrameDescriptor descriptor,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default) =>
        ExportForEncoderAsync(descriptor, vulkanSource: null, auditSink, cancellationToken);

    internal ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
        GpuVideoFrameDescriptor descriptor,
        VulkanOffscreenRenderTarget? vulkanSource,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportStarted,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter)
        });

        var shared = D3D11SharedTextureFactory.CreateSharedTexture(
            _device,
            (uint)descriptor.Width,
            (uint)descriptor.Height);

        if (vulkanSource is not null)
        {
            if (_vulkanDevice is null)
                throw new InvalidOperationException("Vulkan device is required when exporting from an offscreen target.");

            VulkanD3D11ExportBlit.CopyOffscreenToSharedTexture(
                _vulkanDevice,
                vulkanSource,
                shared,
                cancellationToken);
        }

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter),
            Detail = vulkanSource is null
                ? "D3D11 shared NT handle encoder input surface created without CPU staging."
                : "Vulkan offscreen target copied to D3D11 shared encoder surface via GPU blit."
        });

        var leaseDescriptor = new GpuVideoFrameDescriptor
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Format = descriptor.Format,
            TransportKind = MediaTransportKind.GpuSurface
        };

        var lease = HardwareEncoderInputLease.CreateWithBackendSurface(
            leaseDescriptor,
            shared,
            shared.Dispose);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter)
        });

        return ValueTask.FromResult(lease);
    }

    public void Dispose() => _disposed = true;
}

public static class WindowsGpuExportProofDiagnostics
{
    public const string DiagnosticId = "MF-GPU-EXPORT-PROOF";
}
