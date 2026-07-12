using System.Buffers.Binary;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Stream;

public sealed class FlvPacket
{
    public required ReadOnlyMemory<byte> Data { get; init; }

    public required EncodedVideoCodec Codec { get; init; }

    public required EncodedVideoBitstreamFormat BitstreamFormat { get; init; }

    public bool IsKeyFrame { get; init; }

    public bool IsCodecConfiguration { get; init; }

    public TimeSpan Timestamp { get; init; }
}

public sealed class FlvPacketizer
{
    public FlvPacket Packetize(EncodedVideoPacket packet)
    {
        var packets = Packetize(packet, includeCodecConfiguration: false);
        return packets.Count == 0
            ? throw new InvalidOperationException("The encoded packet did not produce an FLV video packet.")
            : packets[0];
    }

    public IReadOnlyList<FlvPacket> Packetize(
        EncodedVideoPacket packet,
        bool includeCodecConfiguration)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"FLV/RTMP currently accepts H.264 packets, not '{packet.Codec}'.");

        if (packet.Data.IsEmpty)
            throw new InvalidOperationException("Cannot packetize an empty encoded video packet.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("FLV/RTMP requires an explicit H.264 bitstream format.");

        var packets = new List<FlvPacket>(includeCodecConfiguration ? 2 : 1);
        if (includeCodecConfiguration)
        {
            var codecConfiguration = BuildAvcDecoderConfiguration(packet);
            if (codecConfiguration.IsEmpty)
                throw new InvalidOperationException("RTMP requires H.264 codec configuration before media packets can be sent.");

            packets.Add(CreatePacket(packet, codecConfiguration, isCodecConfiguration: true));
        }

        packets.Add(CreatePacket(packet, ConvertToAvccPayload(packet), isCodecConfiguration: false));
        return packets;
    }

    private static FlvPacket CreatePacket(
        EncodedVideoPacket packet,
        ReadOnlyMemory<byte> avcPayload,
        bool isCodecConfiguration)
    {
        var payload = new byte[5 + avcPayload.Length];
        payload[0] = (byte)(((packet.IsKeyFrame || isCodecConfiguration ? 1 : 2) << 4) | 7);
        payload[1] = isCodecConfiguration ? (byte)0 : (byte)1;
        payload[2] = 0;
        payload[3] = 0;
        payload[4] = 0;
        avcPayload.Span.CopyTo(payload.AsSpan(5));

        return new FlvPacket
        {
            Data = payload,
            Codec = packet.Codec,
            BitstreamFormat = EncodedVideoBitstreamFormat.Avcc,
            IsKeyFrame = packet.IsKeyFrame || isCodecConfiguration,
            IsCodecConfiguration = isCodecConfiguration,
            Timestamp = packet.PresentationTime
        };
    }

    private static ReadOnlyMemory<byte> ConvertToAvccPayload(EncodedVideoPacket packet)
    {
        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Avcc)
            return packet.Data;

        var nalUnits = H264NalUtilities.ExtractAnnexBNalUnits(packet.Data.Span);
        if (nalUnits.Count == 0)
            throw new InvalidOperationException("H.264 Annex-B packet does not contain any NAL units.");

        using var output = new MemoryStream(packet.Data.Length);
        Span<byte> lengthPrefix = stackalloc byte[4];
        foreach (var nalUnit in nalUnits)
        {
            BinaryPrimitives.WriteUInt32BigEndian(lengthPrefix, (uint)nalUnit.Length);
            output.Write(lengthPrefix);
            output.Write(nalUnit);
        }

        return output.ToArray();
    }

    private static ReadOnlyMemory<byte> BuildAvcDecoderConfiguration(EncodedVideoPacket packet)
    {
        if (!packet.CodecConfiguration.IsEmpty)
            return packet.CodecConfiguration;

        if (packet.BitstreamFormat != EncodedVideoBitstreamFormat.AnnexB)
            return ReadOnlyMemory<byte>.Empty;

        byte[]? sps = null;
        byte[]? pps = null;
        foreach (var nalUnit in H264NalUtilities.ExtractAnnexBNalUnits(packet.Data.Span))
        {
            if (nalUnit.Length == 0)
                continue;

            var nalType = nalUnit[0] & 0x1F;
            if (nalType == 7 && sps is null)
                sps = nalUnit;
            else if (nalType == 8 && pps is null)
                pps = nalUnit;
        }

        if (sps is null || pps is null || sps.Length < 4)
            return ReadOnlyMemory<byte>.Empty;

        using var output = new MemoryStream(11 + sps.Length + pps.Length);
        output.WriteByte(1);
        output.WriteByte(sps[1]);
        output.WriteByte(sps[2]);
        output.WriteByte(sps[3]);
        output.WriteByte(0xFF);
        output.WriteByte(0xE1);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)sps.Length);
        output.Write(length);
        output.Write(sps);
        output.WriteByte(1);
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)pps.Length);
        output.Write(length);
        output.Write(pps);

        return output.ToArray();
    }
}
