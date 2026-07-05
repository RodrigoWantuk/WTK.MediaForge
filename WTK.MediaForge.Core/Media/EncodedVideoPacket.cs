namespace WTK.MediaForge.Core.Media;

public enum EncodedVideoCodec
{
    Unknown,
    H264,
    H265,
    Av1,
    Vp9
}

public sealed class EncodedVideoPacket
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public TimeSpan PresentationTime { get; init; }

    public bool IsKeyFrame { get; init; }
}
