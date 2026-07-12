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
        var mdatDataOffset = checked((uint)(ftyp.Length + placeholderMoov.Length + 8));
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

        var bytes = File.ReadAllBytes(path);
        if (!TryFindBox(bytes, 0, bytes.Length, "ftyp", out _) ||
            !TryFindBox(bytes, 0, bytes.Length, "moov", out var moov) ||
            !TryFindBox(bytes, 0, bytes.Length, "mdat", out var mdat))
        {
            return false;
        }

        if (!TryFindBox(bytes, moov.ContentOffset, moov.EndOffset, "trak", out var trak) ||
            !TryFindBox(bytes, trak.ContentOffset, trak.EndOffset, "mdia", out var mdia) ||
            !TryFindBox(bytes, mdia.ContentOffset, mdia.EndOffset, "minf", out var minf) ||
            !TryFindBox(bytes, minf.ContentOffset, minf.EndOffset, "stbl", out var stbl) ||
            !TryFindBox(bytes, stbl.ContentOffset, stbl.EndOffset, "stsd", out var stsd) ||
            !TryFindAvc1(bytes, stsd, out var avc1))
        {
            return false;
        }

        var avc1Header = avc1.ContentOffset;
        if (avc1Header + 32 > avc1.EndOffset)
            return false;

        var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(avc1Header + 24, 2));
        var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(avc1Header + 26, 2));
        if (width != expectedTrack.Width || height != expectedTrack.Height)
            return false;

        if (!TryFindBox(bytes, avc1.ContentOffset + 78, avc1.EndOffset, "avcC", out _) ||
            !TryFindBox(bytes, stbl.ContentOffset, stbl.EndOffset, "stsz", out var stsz) ||
            !TryFindBox(bytes, stbl.ContentOffset, stbl.EndOffset, "stco", out var stco))
        {
            return false;
        }

        if (stsz.ContentOffset + 12 > stsz.EndOffset ||
            stco.ContentOffset + 12 > stco.EndOffset)
        {
            return false;
        }

        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stsz.ContentOffset + 8, 4));
        if (sampleCount < minimumSampleCount)
            return false;

        var chunkCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stco.ContentOffset + 4, 4));
        if (chunkCount != 1)
            return false;

        var firstChunkOffset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(stco.ContentOffset + 8, 4));
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

    private static void WriteMdatFromFile(System.IO.Stream output, string avccSamplePayloadPath)
    {
        var length = checked((uint)new FileInfo(avccSamplePayloadPath).Length);
        WriteUInt32(output, checked(length + 8));
        output.Write(Encoding.ASCII.GetBytes("mdat"));

        using var input = File.OpenRead(avccSamplePayloadPath);
        input.CopyTo(output);
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

    private readonly record struct BoxInfo(int Offset, int Size, int ContentOffset, int EndOffset);

    private static bool TryFindAvc1(byte[] bytes, BoxInfo stsd, out BoxInfo avc1)
    {
        avc1 = default;
        var offset = stsd.ContentOffset + 8;
        if (offset + 8 > stsd.EndOffset)
            return false;

        return TryReadBox(bytes, offset, stsd.EndOffset, out avc1) &&
            IsBoxType(bytes, avc1, "avc1");
    }

    private static bool TryFindBox(
        byte[] bytes,
        int startOffset,
        int endOffset,
        string type,
        out BoxInfo box)
    {
        box = default;
        var offset = startOffset;
        while (TryReadBox(bytes, offset, endOffset, out var candidate))
        {
            if (IsBoxType(bytes, candidate, type))
            {
                box = candidate;
                return true;
            }

            offset = candidate.EndOffset;
        }

        return false;
    }

    private static bool TryReadBox(byte[] bytes, int offset, int endOffset, out BoxInfo box)
    {
        box = default;
        if (offset < 0 || offset + 8 > endOffset || endOffset > bytes.Length)
            return false;

        var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        if (size < 8 || offset + size > endOffset)
            return false;

        box = new BoxInfo(offset, checked((int)size), offset + 8, offset + checked((int)size));
        return true;
    }

    private static bool IsBoxType(byte[] bytes, BoxInfo box, string type) =>
        Encoding.ASCII.GetString(bytes.AsSpan(box.Offset + 4, 4)).Equals(type, StringComparison.Ordinal);

    private static void WriteUInt32(System.IO.Stream stream, uint value)
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
