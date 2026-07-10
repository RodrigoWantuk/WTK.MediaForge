using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class D3D11BgraToNv12Converter : IHardwareEncoderFormatConverter
{
    private const bool GpuConversionPassImplemented = false;

    public bool CanConvert(GpuVideoFrameDescriptor source, HardwareEncoderInputRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requirement);

        var isSupportedRequest = source.TransportKind == MediaTransportKind.GpuSurface
            && requirement.RequiresGpuSurface
            && source.Width == requirement.Width
            && source.Height == requirement.Height
            && IsBgra(source.Format)
            && IsNv12(requirement.PixelFormat);

        return isSupportedRequest && GpuConversionPassImplemented;
    }

    public ValueTask<HardwareEncoderInputLease> ConvertAsync(
        GpuTextureLease sourceTexture,
        HardwareEncoderInputRequirement requirement,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTexture);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(auditSink);
        cancellationToken.ThrowIfCancellationRequested();

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.GpuFormatConversionUnavailable,
            Source = nameof(D3D11BgraToNv12Converter),
            EvidenceKind = MediaTransportAuditEvidenceKind.ContractOnly,
            Detail = "BGRA/RGBA to NV12 conversion requires a real GPU conversion pass; CPU staging fallback is prohibited."
        });

        throw new NotSupportedException(
            "D3D11 BGRA/RGBA to NV12 encoder format conversion is unavailable until a GPU conversion pass is implemented.");
    }

    private static bool IsBgra(string format) =>
        format.Equals("B8G8R8A8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("BGRA8_UNORM", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("Bgra8Unorm", StringComparison.OrdinalIgnoreCase);

    private static bool IsNv12(string format) =>
        format.Equals("NV12", StringComparison.OrdinalIgnoreCase);
}
