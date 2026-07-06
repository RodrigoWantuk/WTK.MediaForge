using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Interop;

namespace WTK.MediaForge.Core.Media.Decode;

public sealed class HardwareDecoderInfo
{
    public required string Name { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public required string Backend { get; init; }

    public bool ProducesGpuSurface { get; init; } = true;
}

public sealed class DecodeFrameContext
{
    public required EncodedVideoPacket Packet { get; init; }

    public long FrameNumber { get; init; }

    public TimeSpan PresentationTime { get; init; }

    public CancellationToken CancellationToken { get; init; }
}

public interface IHardwareVideoDecoder : IAsyncDisposable
{
    HardwareDecoderInfo Info { get; }

    ValueTask OpenAsync(HardwareDecodeOpenContext context, IMediaTransportAuditSink auditSink);

    ValueTask<DecodedGpuFrame?> DecodeAsync(
        DecodeFrameContext context,
        IMediaTransportAuditSink auditSink);

    ValueTask FlushAsync(IMediaTransportAuditSink auditSink);
}
