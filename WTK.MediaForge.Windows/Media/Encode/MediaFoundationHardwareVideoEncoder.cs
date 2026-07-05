using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Windows.Media.Encode;

/// <summary>
/// Media Foundation hardware encoder skeleton. GPU surface in, H.264 packets out.
/// </summary>
public sealed class MediaFoundationHardwareVideoEncoder : IHardwareVideoEncoder
{
    private readonly HardwareEncoderInfo _info;
    private readonly HardwareEncoderInputRequirement _inputRequirement;
    private bool _disposed;

    public MediaFoundationHardwareVideoEncoder(
        int width,
        int height,
        string pixelFormat = "NV12")
    {
        _info = new HardwareEncoderInfo
        {
            Name = "Media Foundation H.264 Hardware MFT",
            Codec = EncodedVideoCodec.H264,
            Backend = "MediaFoundation-HardwareMft",
            AcceptsGpuSurfaceInput = true
        };

        _inputRequirement = new HardwareEncoderInputRequirement
        {
            Width = width,
            Height = height,
            PixelFormat = pixelFormat,
            RequiresGpuSurface = true
        };
    }

    public HardwareEncoderInfo Info => _info;

    public HardwareEncoderInputRequirement InputRequirement => _inputRequirement;

    public ValueTask<EncodedVideoPacket?> EncodeAsync(
        EncodeFrameContext context,
        IMediaTransportAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(auditSink);

        auditSink.Record(new MediaTransportAuditEvent
        {
            Kind = MediaTransportAuditEventKind.HardwareEncoderInputLeaseCreated,
            Source = nameof(MediaFoundationHardwareVideoEncoder),
            Detail = "MF hardware encoder received GPU input lease (skeleton)."
        });

        return ValueTask.FromResult<EncodedVideoPacket?>(null);
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
