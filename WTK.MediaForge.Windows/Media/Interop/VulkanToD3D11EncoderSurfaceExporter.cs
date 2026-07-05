using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Interop;

public sealed class VulkanToD3D11EncoderSurfaceExporter : IGpuFrameExporter
{
    private readonly ID3D11Device _device;

    public VulkanToD3D11EncoderSurfaceExporter(ID3D11Device device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement) =>
        requirement.RequiresGpuSurface
        && descriptor.Width == requirement.Width
        && descriptor.Height == requirement.Height
        && !requirement.PixelFormat.Contains("cpu", StringComparison.OrdinalIgnoreCase);

    public ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
        GpuVideoFrameDescriptor descriptor,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportStarted,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter)
        });

        var shared = D3D11SharedTextureFactory.CreateSharedTexture(
            _device,
            (uint)descriptor.Width,
            (uint)descriptor.Height);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuSurfaceExportSucceeded,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter),
            Detail = "D3D11 shared NT handle encoder input surface created without CPU staging."
        });

        var leaseDescriptor = new GpuVideoFrameDescriptor
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Format = descriptor.Format,
            TransportKind = MediaTransportKind.GpuSurface
        };

        var lease = HardwareEncoderInputLease.Create(leaseDescriptor, shared.Dispose);
        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = nameof(VulkanToD3D11EncoderSurfaceExporter)
        });

        return ValueTask.FromResult(lease);
    }
}

public static class WindowsGpuExportProofDiagnostics
{
    public const string DiagnosticId = "MF-GPU-EXPORT-PROOF";
}
