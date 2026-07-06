using WTK.MediaForge.Core.Gpu.Resources;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Core.Media.Encode;

public sealed class HardwareEncoderInfo
{
    public required string Name { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public required string Backend { get; init; }

    public bool AcceptsGpuSurfaceInput { get; init; } = true;
}

public sealed class EncodeFrameContext
{
    public required HardwareEncoderInputLease InputLease { get; init; }

    public long FrameNumber { get; init; }

    public TimeSpan PresentationTime { get; init; }

    public CancellationToken CancellationToken { get; init; }
}

public interface IHardwareVideoEncoder : IAsyncDisposable
{
    HardwareEncoderInfo Info { get; }

    HardwareEncoderInputRequirement InputRequirement { get; }

    ValueTask<EncodedVideoPacket?> EncodeAsync(
        EncodeFrameContext context,
        IMediaTransportAuditSink auditSink);

    ValueTask<EncodedVideoPacket?> SubmitFrameAsync(
        GpuTextureLease textureLease,
        HardwareEncodeFrameContext context,
        IGpuFrameExporter frameExporter,
        IMediaTransportAuditSink auditSink);
}
