using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Media.Stream;

public sealed class FlvPacket
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public bool IsKeyFrame { get; init; }

    public TimeSpan Timestamp { get; init; }
}

public sealed class FlvPacketizer
{
    public FlvPacket Packetize(EncodedVideoPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        return new FlvPacket
        {
            Data = packet.Data,
            Codec = packet.Codec,
            IsKeyFrame = packet.IsKeyFrame,
            Timestamp = packet.PresentationTime
        };
    }
}
