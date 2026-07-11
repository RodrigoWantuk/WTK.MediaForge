using System.Buffers.Binary;
using System.Text;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Mux;

internal static class IsoBmffMp4Writer
{
    public static void WriteMp4(string outputPath, IReadOnlyList<EncodedVideoPacket> packets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(packets);

        ValidatePackets(packets);

        var ftyp = BuildFtyp();
        var mdatBytes = BuildMdat(
            packets,
            out var sampleDurations,
            out var syncSampleIndices,
            out var sampleSizes);
        var placeholderMoov = BuildMoov(packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset: 0);
        var mdatDataOffset = checked((uint)(ftyp.Length + placeholderMoov.Length + 8));
        var moov = BuildMoov(packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset);

        using var stream = File.Create(outputPath);
        stream.Write(ftyp);
        stream.Write(moov);
        stream.Write(mdatBytes);
    }

    public static bool HasExperimentalBoxStructure(string path)
    {
        if (!File.Exists(path))
            return false;

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) < 8)
            return false;

        var boxType = Encoding.ASCII.GetString(header[4..8]);
        return boxType == "ftyp" && new FileInfo(path).Length > 128;
    }

    private static byte[] BuildFtyp()
    {
        using var content = new MemoryStream();
        content.Write(Encoding.ASCII.GetBytes("isom"));
        WriteUInt32(content, 0x00000200);
        content.Write(Encoding.ASCII.GetBytes("isomiso2avc1mp41"));
        return WrapBox("ftyp", content.ToArray());
    }

    private static byte[] BuildMoov(
        IReadOnlyList<EncodedVideoPacket> packets,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        WriteMvhd(content, sampleDurations);
        WriteTrak(content, packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset);
        return WrapBox("moov", content.ToArray());
    }

    private static void WriteMvhd(MemoryStream writer, IReadOnlyList<uint> sampleDurations)
    {
        using var content = new MemoryStream();
        content.Write(new byte[12]);
        WriteUInt32(content, 1_000);
        WriteUInt32(content, sampleDurations.Aggregate(0u, static (sum, value) => sum + value));
        content.Write(new byte[]
        {
            0x00, 0x01, 0x00, 0x00,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02
        });

        writer.Write(WrapBox("mvhd", content.ToArray()));
    }

    private static void WriteTrak(
        MemoryStream writer,
        IReadOnlyList<EncodedVideoPacket> packets,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        WriteTkhd(content, sampleDurations);
        WriteMdia(content, packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset);
        writer.Write(WrapBox("trak", content.ToArray()));
    }

    private static void WriteTkhd(MemoryStream writer, IReadOnlyList<uint> sampleDurations)
    {
        using var content = new MemoryStream();
        content.Write(new byte[] { 0x00, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 });
        content.Write(new byte[12]);
        WriteUInt32(content, sampleDurations.Aggregate(0u, static (sum, value) => sum + value));
        content.Write(new byte[52]);
        writer.Write(WrapBox("tkhd", content.ToArray()));
    }

    private static void WriteMdia(
        MemoryStream writer,
        IReadOnlyList<EncodedVideoPacket> packets,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        WriteMdhd(content, sampleDurations);
        WriteHdlr(content);
        WriteMinf(content, packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset);
        writer.Write(WrapBox("mdia", content.ToArray()));
    }

    private static void WriteMdhd(MemoryStream writer, IReadOnlyList<uint> sampleDurations)
    {
        using var content = new MemoryStream();
        content.Write(new byte[12]);
        WriteUInt32(content, 1_000);
        WriteUInt32(content, sampleDurations.Aggregate(0u, static (sum, value) => sum + value));
        content.Write(new byte[] { 0x55, 0xC4, 0x00, 0x00 });
        writer.Write(WrapBox("mdhd", content.ToArray()));
    }

    private static void WriteHdlr(MemoryStream writer)
    {
        using var content = new MemoryStream();
        content.Write(new byte[8]);
        content.Write(Encoding.ASCII.GetBytes("vide"));
        content.Write(new byte[12]);
        content.Write(Encoding.ASCII.GetBytes("VideoHandler"));
        content.WriteByte(0);
        writer.Write(WrapBox("hdlr", content.ToArray()));
    }

    private static void WriteMinf(
        MemoryStream writer,
        IReadOnlyList<EncodedVideoPacket> packets,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        content.Write(WrapBox("vmhd", [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        WriteDinf(content);
        WriteStbl(content, packets, sampleDurations, syncSampleIndices, sampleSizes, mdatDataOffset);
        writer.Write(WrapBox("minf", content.ToArray()));
    }

    private static void WriteDinf(MemoryStream writer)
    {
        using var drefContent = new MemoryStream();
        drefContent.Write(new byte[4]);
        WriteUInt32(drefContent, 1);
        drefContent.Write(WrapBox("url ", [0x00, 0x00, 0x00, 0x01]));

        using var dinfContent = new MemoryStream();
        dinfContent.Write(WrapBox("dref", drefContent.ToArray()));
        writer.Write(WrapBox("dinf", dinfContent.ToArray()));
    }

    private static void WriteStbl(
        MemoryStream writer,
        IReadOnlyList<EncodedVideoPacket> packets,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        WriteStsd(content, packets);
        WriteStts(content, sampleDurations);
        WriteStss(content, syncSampleIndices);
        WriteStsc(content);
        WriteStsz(content, sampleSizes);
        WriteStco(content, mdatDataOffset);
        writer.Write(WrapBox("stbl", content.ToArray()));
    }

    private static void WriteStsd(MemoryStream writer, IReadOnlyList<EncodedVideoPacket> packets)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, 1);

        using var avc1 = new MemoryStream();
        avc1.Write(new byte[78]);
        avc1.Write(WrapBox("avcC", BuildAvcC(packets)));
        content.Write(WrapBox("avc1", avc1.ToArray()));
        writer.Write(WrapBox("stsd", content.ToArray()));
    }

    private static byte[] BuildAvcC(IReadOnlyList<EncodedVideoPacket> packets)
    {
        var configuration = packets
            .Select(packet => packet.CodecConfiguration)
            .FirstOrDefault(configuration => !configuration.IsEmpty);
        if (!configuration.IsEmpty)
            return configuration.ToArray();

        if (!TryFindParameterSets(packets, out var sps, out var pps))
            throw new InvalidOperationException("Cannot build MP4 avcC without H.264 SPS and PPS data.");

        if (sps.Length < 4)
            throw new InvalidOperationException("H.264 SPS is too short to build MP4 avcC data.");

        using var content = new MemoryStream();
        content.WriteByte(0x01);
        content.Write(sps.AsSpan(1, Math.Min(3, sps.Length - 1)));
        content.WriteByte(0xFF);
        content.WriteByte(0xE1);
        WriteUInt16(content, (ushort)sps.Length);
        content.Write(sps);
        content.WriteByte(0x01);
        WriteUInt16(content, (ushort)pps.Length);
        content.Write(pps);
        return content.ToArray();
    }

    private static void WriteStts(MemoryStream writer, IReadOnlyList<uint> sampleDurations)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, (uint)sampleDurations.Count);
        foreach (var duration in sampleDurations)
        {
            WriteUInt32(content, 1);
            WriteUInt32(content, duration);
        }

        writer.Write(WrapBox("stts", content.ToArray()));
    }

    private static void WriteStss(MemoryStream writer, IReadOnlyList<uint> syncSampleIndices)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, (uint)syncSampleIndices.Count);
        foreach (var index in syncSampleIndices)
            WriteUInt32(content, index);

        writer.Write(WrapBox("stss", content.ToArray()));
    }

    private static void WriteStsc(MemoryStream writer)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, 1);
        WriteUInt32(content, 1);
        WriteUInt32(content, 1);
        WriteUInt32(content, 1);
        writer.Write(WrapBox("stsc", content.ToArray()));
    }

    private static void WriteStsz(MemoryStream writer, IReadOnlyList<uint> sampleSizes)
    {
        using var content = new MemoryStream();
        content.Write(new byte[8]);
        WriteUInt32(content, (uint)sampleSizes.Count);
        foreach (var sampleSize in sampleSizes)
            WriteUInt32(content, sampleSize);

        writer.Write(WrapBox("stsz", content.ToArray()));
    }

    private static void WriteStco(MemoryStream writer, uint mdatDataOffset)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, 1);
        WriteUInt32(content, mdatDataOffset);
        writer.Write(WrapBox("stco", content.ToArray()));
    }

    private static byte[] BuildMdat(
        IReadOnlyList<EncodedVideoPacket> packets,
        out List<uint> sampleDurations,
        out List<uint> syncSampleIndices,
        out List<uint> sampleSizes)
    {
        sampleDurations = [];
        syncSampleIndices = [];
        sampleSizes = [];

        using var payload = new MemoryStream();
        for (var index = 0; index < packets.Count; index++)
        {
            var packet = packets[index];
            var sampleBytes = packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB
                ? ConvertAnnexBToAvcc(packet.Data.Span)
                : packet.Data.ToArray();
            payload.Write(sampleBytes);
            sampleSizes.Add((uint)sampleBytes.Length);
            sampleDurations.Add(ResolveSampleDurationMilliseconds(packets, index));

            if (packet.IsKeyFrame || (H264NalUtilities.TryGetFirstNalType(packet.Data.Span, out var nalType) && nalType == 5))
                syncSampleIndices.Add((uint)(index + 1));
        }

        if (syncSampleIndices.Count == 0)
            syncSampleIndices.Add(1);

        return WrapBox("mdat", payload.ToArray());
    }

    private static byte[] ConvertAnnexBToAvcc(ReadOnlySpan<byte> annexB)
    {
        using var content = new MemoryStream();
        foreach (var nal in H264NalUtilities.ExtractAnnexBNalUnits(annexB))
        {
            WriteUInt32(content, (uint)nal.Length);
            content.Write(nal);
        }

        return content.ToArray();
    }

    private static void ValidatePackets(IReadOnlyList<EncodedVideoPacket> packets)
    {
        if (packets.Count == 0)
            throw new InvalidOperationException("Cannot write MP4 without encoded packets.");

        EncodedVideoBitstreamFormat? expectedFormat = null;
        var avccHasCodecConfiguration = false;
        for (var index = 0; index < packets.Count; index++)
        {
            var packet = packets[index] ??
                throw new InvalidOperationException("Cannot write MP4 with a null encoded packet.");

            expectedFormat ??= packet.BitstreamFormat;
            avccHasCodecConfiguration |= !packet.CodecConfiguration.IsEmpty;

            if (packet.Codec != EncodedVideoCodec.H264)
                throw new NotSupportedException($"MP4 prototype writer currently accepts H.264 packets, not '{packet.Codec}'.");

            if (packet.Data.IsEmpty)
                throw new InvalidOperationException("Cannot write MP4 with an empty encoded packet.");

            if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
                throw new NotSupportedException("MP4 prototype writer requires an explicit H.264 bitstream format.");

            if (packet.BitstreamFormat != expectedFormat.Value)
                throw new NotSupportedException("MP4 prototype writer does not support mixed H.264 bitstream formats in one file.");

            if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB)
            {
                if (!H264NalUtilities.ContainsValidStartCode(packet.Data.Span))
                    throw new InvalidOperationException("Annex-B H.264 packet does not contain a valid start code.");

                if (H264NalUtilities.ExtractAnnexBNalUnits(packet.Data.Span).Count == 0)
                    throw new InvalidOperationException("Annex-B H.264 packet does not contain a NAL payload.");
            }
        }

        if (expectedFormat == EncodedVideoBitstreamFormat.Avcc && !avccHasCodecConfiguration)
            throw new NotSupportedException("AVCC H.264 packets require codec configuration data for MP4 writing.");
    }

    private static uint ResolveSampleDurationMilliseconds(
        IReadOnlyList<EncodedVideoPacket> packets,
        int index)
    {
        var packet = packets[index];
        if (packet.Duration > TimeSpan.Zero)
            return MillisecondsAtLeastOne(packet.Duration);

        if (index + 1 < packets.Count &&
            packets[index + 1].PresentationTime > packet.PresentationTime)
        {
            return MillisecondsAtLeastOne(packets[index + 1].PresentationTime - packet.PresentationTime);
        }

        return 1_000u / 30u;
    }

    private static uint MillisecondsAtLeastOne(TimeSpan value) =>
        (uint)Math.Max(1, (int)Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero));

    private static bool TryFindParameterSets(
        IReadOnlyList<EncodedVideoPacket> packets,
        out byte[] sps,
        out byte[] pps)
    {
        sps = [];
        pps = [];

        foreach (var packet in packets.OrderByDescending(static packet => packet.IsKeyFrame))
        {
            foreach (var nal in H264NalUtilities.ExtractAnnexBNalUnits(packet.Data.Span))
            {
                if (nal.Length == 0)
                    continue;

                var nalType = nal[0] & 0x1F;
                if (nalType == 7 && sps.Length == 0)
                    sps = nal;
                else if (nalType == 8 && pps.Length == 0)
                    pps = nal;

                if (sps.Length > 0 && pps.Length > 0)
                    return true;
            }
        }

        return false;
    }

    private static byte[] WrapBox(string type, byte[] content)
    {
        var size = checked(8 + content.Length);
        var box = new byte[size];
        BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(0, 4), size);
        Encoding.ASCII.GetBytes(type, box.AsSpan(4, 4));
        content.CopyTo(box.AsSpan(8));
        return box;
    }

    private static void WriteUInt32(MemoryStream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
