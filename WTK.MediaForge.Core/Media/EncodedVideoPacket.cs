namespace WTK.MediaForge.Core.Media;

public enum EncodedVideoCodec
{
    Unknown,
    H264,
    H265,
    Av1,
    Vp9
}

public enum EncodedVideoBitstreamFormat
{
    Unknown,
    AnnexB,
    Avcc
}

public sealed class EncodedVideoPacket
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public EncodedVideoBitstreamFormat BitstreamFormat { get; init; } =
        EncodedVideoBitstreamFormat.Unknown;

    public TimeSpan PresentationTime { get; init; }

    public TimeSpan Duration { get; init; }

    public bool IsKeyFrame { get; init; }

    public ReadOnlyMemory<byte> CodecConfiguration { get; init; } =
        ReadOnlyMemory<byte>.Empty;
}
