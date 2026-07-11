using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Media.Stream;

public sealed class FlvPacket
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public required EncodedVideoBitstreamFormat BitstreamFormat { get; init; }

    public bool IsKeyFrame { get; init; }

    public TimeSpan Timestamp { get; init; }
}

public sealed class FlvPacketizer
{
    public FlvPacket Packetize(EncodedVideoPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"FLV prototype packetizer currently accepts H.264 packets, not '{packet.Codec}'.");

        if (packet.Data.IsEmpty)
            throw new InvalidOperationException("Cannot packetize an empty encoded video packet.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("FLV prototype packetizer requires an explicit H.264 bitstream format.");

        return new FlvPacket
        {
            Data = packet.Data,
            Codec = packet.Codec,
            BitstreamFormat = packet.BitstreamFormat,
            IsKeyFrame = packet.IsKeyFrame,
            Timestamp = packet.PresentationTime
        };
    }
}
