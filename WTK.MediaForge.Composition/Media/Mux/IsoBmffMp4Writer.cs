using System.Buffers.Binary;
using System.Text;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Mux;

internal static class IsoBmffMp4Writer
{
    public readonly record struct SampleMetadata(
        uint DurationMilliseconds,
        uint Size,
        bool IsKeyFrame);

    public readonly record struct TrackMetadata(uint Width, uint Height)
    {
        public static TrackMetadata Default { get; } = new(1920, 1080);
    }

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
        var avcC = BuildAvcC(packets);
        var placeholderMoov = BuildMoov(sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset: 0, TrackMetadata.Default);
        var mdatDataOffset = checked((uint)(ftyp.Length + placeholderMoov.Length + 8));
        var moov = BuildMoov(sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset, TrackMetadata.Default);

        using var stream = File.Create(outputPath);
        stream.Write(ftyp);
        stream.Write(moov);
        stream.Write(mdatBytes);
    }

    public static void WriteMp4FromAvccSamples(
        string outputPath,
        string avccSamplePayloadPath,
        IReadOnlyList<SampleMetadata> samples,
        ReadOnlyMemory<byte> avcC,
        TrackMetadata track)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(avccSamplePayloadPath);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
            throw new InvalidOperationException("Cannot write MP4 without encoded samples.");

        if (avcC.IsEmpty)
            throw new InvalidOperationException("Cannot write MP4 without H.264 avcC codec configuration.");

        if (!File.Exists(avccSamplePayloadPath))
            throw new FileNotFoundException("MP4 sample payload file was not found.", avccSamplePayloadPath);

        var sampleDurations = samples.Select(static sample => sample.DurationMilliseconds).ToArray();
        var sampleSizes = samples.Select(static sample => sample.Size).ToArray();
        var syncSampleIndices = samples
            .Select((sample, index) => (sample, index))
            .Where(static item => item.sample.IsKeyFrame)
            .Select(static item => (uint)(item.index + 1))
            .ToArray();
        if (syncSampleIndices.Length == 0)
            syncSampleIndices = [1];

        var ftyp = BuildFtyp();
        ValidateTrack(track);

        var placeholderMoov = BuildMoov(sampleDurations, syncSampleIndices, sampleSizes, avcC.ToArray(), mdatDataOffset: 0, track);
        var mdatHeaderSize = GetMdatHeaderSize(new FileInfo(avccSamplePayloadPath).Length);
        var mdatDataOffset = checked((uint)(ftyp.Length + placeholderMoov.Length + mdatHeaderSize));
        var moov = BuildMoov(sampleDurations, syncSampleIndices, sampleSizes, avcC.ToArray(), mdatDataOffset, track);

        using var output = File.Create(outputPath);
        output.Write(ftyp);
        output.Write(moov);
        WriteMdatFromFile(output, avccSamplePayloadPath);
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

    public static bool HasValidH264BoxStructure(
        string path,
        TrackMetadata expectedTrack,
        int minimumSampleCount)
    {
        if (!File.Exists(path) || minimumSampleCount <= 0)
            return false;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!TryFindBox(stream, 0, stream.Length, "ftyp", out _) ||
            !TryFindBox(stream, 0, stream.Length, "moov", out var moov) ||
            !TryFindBox(stream, 0, stream.Length, "mdat", out var mdat))
        {
            return false;
        }

        if (!TryFindBox(stream, moov.ContentOffset, moov.EndOffset, "trak", out var trak) ||
            !TryFindBox(stream, trak.ContentOffset, trak.EndOffset, "mdia", out var mdia) ||
            !TryFindBox(stream, mdia.ContentOffset, mdia.EndOffset, "minf", out var minf) ||
            !TryFindBox(stream, minf.ContentOffset, minf.EndOffset, "stbl", out var stbl) ||
            !TryFindBox(stream, stbl.ContentOffset, stbl.EndOffset, "stsd", out var stsd) ||
            !TryFindAvc1(stream, stsd, out var avc1))
        {
            return false;
        }

        var avc1Header = avc1.ContentOffset;
        if (avc1Header + 32 > avc1.EndOffset)
            return false;

        Span<byte> dimensions = stackalloc byte[4];
        if (!ReadExactly(stream, avc1Header + 24, dimensions))
            return false;

        var width = BinaryPrimitives.ReadUInt16BigEndian(dimensions[..2]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(dimensions[2..]);
        if (width != expectedTrack.Width || height != expectedTrack.Height)
            return false;

        if (!TryFindBox(stream, avc1.ContentOffset + 78, avc1.EndOffset, "avcC", out _) ||
            !TryFindBox(stream, stbl.ContentOffset, stbl.EndOffset, "stsz", out var stsz) ||
            !TryFindBox(stream, stbl.ContentOffset, stbl.EndOffset, "stco", out var stco))
        {
            return false;
        }

        if (stsz.ContentOffset + 12 > stsz.EndOffset ||
            stco.ContentOffset + 12 > stco.EndOffset)
        {
            return false;
        }

        Span<byte> tableHeader = stackalloc byte[12];
        if (!ReadExactly(stream, stsz.ContentOffset, tableHeader))
            return false;

        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(tableHeader[8..12]);
        if (sampleCount < minimumSampleCount)
            return false;

        if (!ReadExactly(stream, stco.ContentOffset, tableHeader))
            return false;

        var chunkCount = BinaryPrimitives.ReadUInt32BigEndian(tableHeader[4..8]);
        if (chunkCount != 1)
            return false;

        var firstChunkOffset = BinaryPrimitives.ReadUInt32BigEndian(tableHeader[8..12]);
        return firstChunkOffset == mdat.ContentOffset;
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
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        byte[] avcC,
        uint mdatDataOffset,
        TrackMetadata track)
    {
        using var content = new MemoryStream();
        WriteMvhd(content, sampleDurations);
        WriteTrak(content, sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset, track);
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
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        byte[] avcC,
        uint mdatDataOffset,
        TrackMetadata track)
    {
        using var content = new MemoryStream();
        WriteTkhd(content, sampleDurations, track);
        WriteMdia(content, sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset, track);
        writer.Write(WrapBox("trak", content.ToArray()));
    }

    private static void WriteTkhd(MemoryStream writer, IReadOnlyList<uint> sampleDurations, TrackMetadata track)
    {
        using var content = new MemoryStream();
        content.Write(new byte[] { 0x00, 0x00, 0x00, 0x07 });
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt32(content, 1);
        WriteUInt32(content, 0);
        WriteUInt32(content, sampleDurations.Aggregate(0u, static (sum, value) => sum + value));
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt16(content, 0);
        WriteUInt16(content, 0);
        WriteUInt16(content, 0);
        WriteUInt16(content, 0);
        WriteUInt32(content, 0x00010000);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0x00010000);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0);
        WriteUInt32(content, 0x40000000);
        WriteUInt32(content, checked(track.Width << 16));
        WriteUInt32(content, checked(track.Height << 16));
        writer.Write(WrapBox("tkhd", content.ToArray()));
    }

    private static void WriteMdia(
        MemoryStream writer,
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        byte[] avcC,
        uint mdatDataOffset,
        TrackMetadata track)
    {
        using var content = new MemoryStream();
        WriteMdhd(content, sampleDurations);
        WriteHdlr(content);
        WriteMinf(content, sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset, track);
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
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        byte[] avcC,
        uint mdatDataOffset,
        TrackMetadata track)
    {
        using var content = new MemoryStream();
        content.Write(WrapBox("vmhd", [0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        WriteDinf(content);
        WriteStbl(content, sampleDurations, syncSampleIndices, sampleSizes, avcC, mdatDataOffset, track);
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
        IReadOnlyList<uint> sampleDurations,
        IReadOnlyList<uint> syncSampleIndices,
        IReadOnlyList<uint> sampleSizes,
        byte[] avcC,
        uint mdatDataOffset,
        TrackMetadata track)
    {
        using var content = new MemoryStream();
        WriteStsd(content, avcC, track);
        WriteStts(content, sampleDurations);
        WriteStss(content, syncSampleIndices);
        WriteStsc(content);
        WriteStsz(content, sampleSizes);
        WriteStco(content, mdatDataOffset);
        writer.Write(WrapBox("stbl", content.ToArray()));
    }

    private static void WriteStsd(MemoryStream writer, byte[] avcCBytes, TrackMetadata track)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        WriteUInt32(content, 1);

        using var avc1 = new MemoryStream();
        avc1.Write(new byte[6]);
        WriteUInt16(avc1, 1);
        avc1.Write(new byte[16]);
        WriteUInt16(avc1, checked((ushort)track.Width));
        WriteUInt16(avc1, checked((ushort)track.Height));
        WriteUInt32(avc1, 0x00480000);
        WriteUInt32(avc1, 0x00480000);
        WriteUInt32(avc1, 0);
        WriteUInt16(avc1, 1);
        avc1.Write(new byte[32]);
        WriteUInt16(avc1, 0x0018);
        WriteUInt16(avc1, 0xFFFF);
        avc1.Write(WrapBox("avcC", avcCBytes));
        content.Write(WrapBox("avc1", avc1.ToArray()));
        writer.Write(WrapBox("stsd", content.ToArray()));
    }

    private static byte[] BuildAvcC(IReadOnlyList<EncodedVideoPacket> packets)
    {
        foreach (var configuration in packets.Select(static packet => packet.CodecConfiguration))
        {
            if (!configuration.IsEmpty &&
                TryNormalizeH264CodecConfiguration(configuration.Span, out var normalized))
            {
                return normalized;
            }
        }

        if (!TryFindParameterSets(packets, out var sps, out var pps))
            throw new InvalidOperationException("Cannot build MP4 avcC without H.264 SPS and PPS data.");

        return BuildAvcCFromParameterSets(sps, pps);
    }

    internal static bool TryNormalizeH264CodecConfiguration(
        ReadOnlySpan<byte> configuration,
        out byte[] avcC)
    {
        if (TryExtractParameterSetsFromAvcC(configuration, out var avcCSps, out var avcCPps))
        {
            avcC = BuildAvcCFromParameterSets(avcCSps, avcCPps);
            return true;
        }

        if (TryFindParameterSets(configuration, out var sps, out var pps))
        {
            avcC = BuildAvcCFromParameterSets(sps, pps);
            return true;
        }

        avcC = [];
        return false;
    }

    private static byte[] BuildAvcCFromParameterSets(byte[] sps, byte[] pps)
    {
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

    private static bool IsValidAvcC(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7 || data[0] != 0x01)
            return false;

        var offset = 6;
        var spsCount = data[5] & 0x1F;
        if (spsCount == 0)
            return false;

        for (var index = 0; index < spsCount; index++)
        {
            if (offset + 2 > data.Length)
                return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2;
            if (length == 0 || offset + length > data.Length)
                return false;

            offset += length;
        }

        if (offset + 1 > data.Length)
            return false;

        var ppsCount = data[offset++];
        if (ppsCount == 0)
            return false;

        for (var index = 0; index < ppsCount; index++)
        {
            if (offset + 2 > data.Length)
                return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2;
            if (length == 0 || offset + length > data.Length)
                return false;

            offset += length;
        }

        return offset <= data.Length;
    }

    private static bool TryExtractParameterSetsFromAvcC(
        ReadOnlySpan<byte> data,
        out byte[] sps,
        out byte[] pps)
    {
        sps = [];
        pps = [];

        if (!IsValidAvcC(data))
            return false;

        var offset = 6;
        var spsCount = data[5] & 0x1F;
        for (var index = 0; index < spsCount; index++)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2;
            if (sps.Length == 0)
                sps = data.Slice(offset, length).ToArray();
            offset += length;
        }

        var ppsCount = data[offset++];
        for (var index = 0; index < ppsCount; index++)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            offset += 2;
            if (pps.Length == 0)
                pps = data.Slice(offset, length).ToArray();
            offset += length;
        }

        return sps.Length > 0 && pps.Length > 0;
    }

    private static void WriteStts(MemoryStream writer, IReadOnlyList<uint> sampleDurations)
    {
        using var content = new MemoryStream();
        content.Write(new byte[4]);
        var entryCountOffset = content.Position;
        WriteUInt32(content, 0);
        var entryCount = 0u;
        for (var index = 0; index < sampleDurations.Count;)
        {
            var duration = sampleDurations[index];
            var runLength = 1u;
            while (index + runLength < sampleDurations.Count &&
                   sampleDurations[index + (int)runLength] == duration)
            {
                runLength++;
            }

            WriteUInt32(content, runLength);
            WriteUInt32(content, duration);
            entryCount++;
            index += checked((int)runLength);
        }

        var endOffset = content.Position;
        content.Position = entryCountOffset;
        WriteUInt32(content, entryCount);
        content.Position = endOffset;

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
                : NormalizeAvccSample(packet.Data.Span);
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

    internal static byte[] ConvertAnnexBToAvcc(ReadOnlySpan<byte> annexB)
    {
        using var content = new MemoryStream();
        var searchOffset = 0;
        while (H264NalUtilities.TryReadNextAnnexBNalUnit(
            annexB,
            searchOffset,
            out searchOffset,
            out _,
            out var nalOffset,
            out var nalLength))
        {
            WriteUInt32(content, (uint)nalLength);
            content.Write(annexB.Slice(nalOffset, nalLength));
        }

        return content.ToArray();
    }

    internal static uint WriteAnnexBAsAvccSample(System.IO.Stream output, ReadOnlySpan<byte> annexB)
    {
        var written = 0u;
        var searchOffset = 0;
        while (H264NalUtilities.TryReadNextAnnexBNalUnit(
            annexB,
            searchOffset,
            out searchOffset,
            out _,
            out var nalOffset,
            out var nalLength))
        {
            WriteUInt32(output, (uint)nalLength);
            output.Write(annexB.Slice(nalOffset, nalLength));
            written = checked(written + 4u + (uint)nalLength);
        }

        if (written == 0)
            throw new InvalidOperationException("Annex-B H.264 packet does not contain a NAL payload.");

        return written;
    }

    internal static uint WriteAvccSample(System.IO.Stream output, ReadOnlySpan<byte> avccOrSingleNal)
    {
        if (IsValidAvccSample(avccOrSingleNal))
        {
            output.Write(avccOrSingleNal);
            return checked((uint)avccOrSingleNal.Length);
        }

        WriteUInt32(output, checked((uint)avccOrSingleNal.Length));
        output.Write(avccOrSingleNal);
        return checked(4u + (uint)avccOrSingleNal.Length);
    }

    private static byte[] NormalizeAvccSample(ReadOnlySpan<byte> avccOrSingleNal)
    {
        if (IsValidAvccSample(avccOrSingleNal))
            return avccOrSingleNal.ToArray();

        using var content = new MemoryStream();
        WriteUInt32(content, checked((uint)avccOrSingleNal.Length));
        content.Write(avccOrSingleNal);
        return content.ToArray();
    }

    private static bool IsValidAvccSample(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        var nalCount = 0;
        while (offset < data.Length)
        {
            if (offset + 4 > data.Length)
                return false;

            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            offset += 4;
            if (length == 0 || length > int.MaxValue || offset + (int)length > data.Length)
                return false;

            offset += (int)length;
            nalCount++;
        }

        return nalCount > 0 && offset == data.Length;
    }

    private static void WriteMdatFromFile(System.IO.Stream output, string avccSamplePayloadPath)
    {
        var length = new FileInfo(avccSamplePayloadPath).Length;
        if (GetMdatHeaderSize(length) == 8)
        {
            WriteUInt32(output, checked((uint)(length + 8)));
            output.Write(Encoding.ASCII.GetBytes("mdat"));
        }
        else
        {
            WriteUInt32(output, 1);
            output.Write(Encoding.ASCII.GetBytes("mdat"));
            WriteUInt64(output, checked((ulong)length + 16));
        }

        using var input = File.OpenRead(avccSamplePayloadPath);
        input.CopyTo(output);
    }

    internal static int GetMdatHeaderSize(long payloadLength)
    {
        if (payloadLength < 0)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));

        return payloadLength <= uint.MaxValue - 8L ? 8 : 16;
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
                throw new NotSupportedException($"MP4 writer currently accepts H.264 packets, not '{packet.Codec}'.");

            if (packet.Data.IsEmpty)
                throw new InvalidOperationException("Cannot write MP4 with an empty encoded packet.");

            if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
                throw new NotSupportedException("MP4 writer requires an explicit H.264 bitstream format.");

            if (packet.BitstreamFormat != expectedFormat.Value)
                throw new NotSupportedException("MP4 writer does not support mixed H.264 bitstream formats in one file.");

            if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB)
            {
                if (!H264NalUtilities.ContainsValidStartCode(packet.Data.Span))
                    throw new InvalidOperationException("Annex-B H.264 packet does not contain a valid start code.");

                if (!H264NalUtilities.ContainsAnnexBNalPayload(packet.Data.Span))
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
            var searchOffset = 0;
            while (H264NalUtilities.TryReadNextAnnexBNalUnit(
                packet.Data.Span,
                searchOffset,
                out searchOffset,
                out _,
                out var nalOffset,
                out var nalLength))
            {
                if (nalLength == 0)
                    continue;

                var nal = packet.Data.Span.Slice(nalOffset, nalLength);
                var nalType = nal[0] & 0x1F;
                if (nalType == 7 && sps.Length == 0)
                    sps = nal.ToArray();
                else if (nalType == 8 && pps.Length == 0)
                    pps = nal.ToArray();

                if (sps.Length > 0 && pps.Length > 0)
                    return true;
            }
        }

        return false;
    }

    private static bool TryFindParameterSets(
        ReadOnlySpan<byte> annexB,
        out byte[] sps,
        out byte[] pps)
    {
        sps = [];
        pps = [];

        var searchOffset = 0;
        while (H264NalUtilities.TryReadNextAnnexBNalUnit(
            annexB,
            searchOffset,
            out searchOffset,
            out _,
            out var nalOffset,
            out var nalLength))
        {
            if (nalLength == 0)
                continue;

            var nal = annexB.Slice(nalOffset, nalLength);
            var nalType = nal[0] & 0x1F;
            if (nalType == 7 && sps.Length == 0)
                sps = nal.ToArray();
            else if (nalType == 8 && pps.Length == 0)
                pps = nal.ToArray();

            if (sps.Length > 0 && pps.Length > 0)
                return true;
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

    private static void ValidateTrack(TrackMetadata track)
    {
        if (track.Width == 0 || track.Height == 0)
            throw new InvalidOperationException("MP4 track dimensions must be positive.");

        if (track.Width > ushort.MaxValue || track.Height > ushort.MaxValue)
            throw new InvalidOperationException("MP4 track dimensions exceed avc1 sample entry limits.");
    }

    private readonly record struct BoxInfo(long Offset, long Size, long ContentOffset, long EndOffset);

    private static bool TryFindAvc1(System.IO.Stream stream, BoxInfo stsd, out BoxInfo avc1)
    {
        avc1 = default;
        var offset = stsd.ContentOffset + 8;
        if (offset + 8 > stsd.EndOffset)
            return false;

        return TryReadBox(stream, offset, stsd.EndOffset, out avc1, out var type) &&
            type == "avc1";
    }

    private static bool TryFindBox(
        System.IO.Stream stream,
        long startOffset,
        long endOffset,
        string type,
        out BoxInfo box)
    {
        box = default;
        var offset = startOffset;
        while (TryReadBox(stream, offset, endOffset, out var candidate, out var candidateType))
        {
            if (candidateType == type)
            {
                box = candidate;
                return true;
            }

            offset = candidate.EndOffset;
        }

        return false;
    }

    private static bool TryReadBox(
        System.IO.Stream stream,
        long offset,
        long endOffset,
        out BoxInfo box,
        out string type)
    {
        box = default;
        type = string.Empty;
        if (offset < 0 || offset + 8 > endOffset || endOffset > stream.Length)
            return false;

        Span<byte> header = stackalloc byte[16];
        if (!ReadExactly(stream, offset, header[..8]))
            return false;

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        type = Encoding.ASCII.GetString(header.Slice(4, 4));
        var headerSize = 8;
        long size;
        if (size32 == 1)
        {
            if (!ReadExactly(stream, offset + 8, header.Slice(8, 8)))
                return false;

            var size64 = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
            if (size64 > long.MaxValue)
                return false;

            size = (long)size64;
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            size = endOffset - offset;
        }
        else
        {
            size = size32;
        }

        if (size < headerSize || offset > endOffset - size)
            return false;

        box = new BoxInfo(offset, size, offset + headerSize, offset + size);
        return true;
    }

    private static bool ReadExactly(System.IO.Stream stream, long offset, Span<byte> destination)
    {
        stream.Position = offset;
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = stream.Read(destination[totalRead..]);
            if (read == 0)
                return false;

            totalRead += read;
        }

        return true;
    }

    private static void WriteUInt32(System.IO.Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(System.IO.Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
