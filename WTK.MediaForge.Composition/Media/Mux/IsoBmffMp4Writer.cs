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

        if (packets.Count == 0)
            throw new InvalidOperationException("Cannot write MP4 without encoded packets.");

        var mdatBytes = BuildMdat(packets, out var sampleDurations, out var syncSampleIndices);
        var moov = BuildMoov(packets, sampleDurations, syncSampleIndices, mdatBytes.Length);
        var ftyp = BuildFtyp();

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
        int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        WriteMvhd(content, sampleDurations);
        WriteTrak(content, packets, sampleDurations, syncSampleIndices, mdatPayloadLength);
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
        int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        WriteTkhd(content, sampleDurations);
        WriteMdia(content, packets, sampleDurations, syncSampleIndices, mdatPayloadLength);
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
        int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        WriteMdhd(content, sampleDurations);
        WriteHdlr(content);
        WriteMinf(content, packets, sampleDurations, syncSampleIndices, mdatPayloadLength);
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
        int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        writer.Write(WrapBox("vmhd", [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        WriteDinf(content);
        WriteStbl(content, packets, sampleDurations, syncSampleIndices, mdatPayloadLength);
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
        int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        WriteStsd(content, packets);
        WriteStts(content, sampleDurations);
        WriteStss(content, syncSampleIndices);
        WriteStsc(content);
        WriteStsz(content, packets);
        WriteStco(content, mdatPayloadLength);
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
        var keyFrame = packets.FirstOrDefault(packet => packet.IsKeyFrame) ?? packets[0];
        var nalUnits = ExtractNalUnits(keyFrame.Data.Span);
        var sps = nalUnits.FirstOrDefault(nal => (nal[0] & 0x1F) == 7) ?? [0x67, 0x42, 0x00, 0x1E, 0xAB, 0x40, 0xF0, 0x28, 0xD3, 0x70];
        var pps = nalUnits.FirstOrDefault(nal => (nal[0] & 0x1F) == 8) ?? [0x68, 0xCE, 0x3C, 0x80];

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

    private static void WriteStsz(MemoryStream writer, IReadOnlyList<EncodedVideoPacket> packets)
    {
        using var content = new MemoryStream();
        content.Write(new byte[8]);
        WriteUInt32(content, (uint)packets.Count);
        foreach (var packet in packets)
            WriteUInt32(content, (uint)packet.Data.Length);

        writer.Write(WrapBox("stsz", content.ToArray()));
    }

    private static void WriteStco(MemoryStream writer, int mdatPayloadLength)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, 1);
        WriteUInt32(content, (uint)(8 + 512 + mdatPayloadLength / 10));
        writer.Write(WrapBox("stco", content.ToArray()));
    }

    private static byte[] BuildMdat(
        IReadOnlyList<EncodedVideoPacket> packets,
        out List<uint> sampleDurations,
        out List<uint> syncSampleIndices)
    {
        sampleDurations = [];
        syncSampleIndices = [];

        using var payload = new MemoryStream();
        for (var index = 0; index < packets.Count; index++)
        {
            var packet = packets[index];
            payload.Write(ConvertAnnexBToAvcc(packet.Data.Span));
            sampleDurations.Add(1_000u / 30u);

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
        foreach (var nal in ExtractNalUnits(annexB))
        {
            WriteUInt32(content, (uint)nal.Length);
            content.Write(nal);
        }

        return content.ToArray();
    }

    private static List<byte[]> ExtractNalUnits(ReadOnlySpan<byte> data)
    {
        var units = new List<byte[]>();
        var index = 0;

        while (index < data.Length)
        {
            var start = FindStartCode(data, index);
            if (start < 0)
                break;

            var next = FindStartCode(data, start + 3);
            var end = next < 0 ? data.Length : next;
            var nalLength = end - start;
            if (nalLength > 0)
                units.Add(data.Slice(start, nalLength).ToArray());

            index = next < 0 ? data.Length : next;
        }

        return units;
    }

    private static int FindStartCode(ReadOnlySpan<byte> data, int offset)
    {
        for (var index = offset; index <= data.Length - 3; index++)
        {
            if (data[index] == 0x00 && data[index + 1] == 0x00 &&
                (data[index + 2] == 0x01 || (index + 3 < data.Length && data[index + 2] == 0x00 && data[index + 3] == 0x01)))
            {
                return index + (data[index + 2] == 0x01 ? 3 : 4);
            }
        }

        return -1;
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
