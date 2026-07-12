using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Mux;

internal sealed class EncodedPacketMp4Muxer : IMp4Muxer
{
    private readonly string _outputPath;
    private readonly string _payloadPath;
    private readonly FileStream _payloadStream;
    private readonly List<IsoBmffMp4Writer.SampleMetadata> _samples = [];
    private byte[] _avcC = [];
    private byte[] _sps = [];
    private byte[] _pps = [];
    private EncodedVideoBitstreamFormat? _bitstreamFormat;
    private bool _finalized;
    private bool _disposed;

    public EncodedPacketMp4Muxer(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _payloadPath = Path.Combine(Path.GetTempPath(), $"mf_mp4_payload_{Guid.NewGuid():N}.bin");
        _payloadStream = File.Create(_payloadPath);
    }

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
            throw new InvalidOperationException("MP4 muxer is already finalized.");

        ValidateProductPacket(packet);

        _bitstreamFormat ??= packet.BitstreamFormat;
        if (packet.BitstreamFormat != _bitstreamFormat.Value)
            throw new NotSupportedException("MP4 recording does not support mixed H.264 bitstream formats in one file.");

        CaptureCodecConfiguration(packet);
        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Avcc && _avcC.Length == 0)
            throw new NotSupportedException("AVCC H.264 packets require codec configuration data for MP4 recording.");

        var sampleBytes = packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB
            ? IsoBmffMp4Writer.ConvertAnnexBToAvcc(packet.Data.Span)
            : packet.Data.ToArray();

        _payloadStream.Write(sampleBytes);
        _samples.Add(new IsoBmffMp4Writer.SampleMetadata(
            ResolveSampleDurationMilliseconds(packet),
            checked((uint)sampleBytes.Length),
            packet.IsKeyFrame || (H264NalUtilities.TryGetFirstNalType(packet.Data.Span, out var nalType) && nalType == 5)));

        return ValueTask.CompletedTask;
    }

    public ValueTask FinalizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
            return ValueTask.CompletedTask;

        _finalized = true;
        _payloadStream.Flush(flushToDisk: true);
        _payloadStream.Dispose();

        var avcC = ResolveAvcC();
        IsoBmffMp4Writer.WriteMp4FromAvccSamples(_outputPath, _payloadPath, _samples, avcC);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _payloadStream.Dispose();
        if (File.Exists(_payloadPath))
            File.Delete(_payloadPath);

        _samples.Clear();
        return ValueTask.CompletedTask;
    }

    private static void ValidateProductPacket(EncodedVideoPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
        {
            throw new NotSupportedException(
                "Product MP4 recording requires packets with BackendOutputValidated evidence.");
        }

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"MP4 recording currently accepts H.264 packets, not '{packet.Codec}'.");

        if (packet.Data.IsEmpty)
            throw new InvalidOperationException("Cannot write MP4 with an empty encoded packet.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("MP4 recording requires packets with an explicit H.264 bitstream format.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB &&
            !H264NalUtilities.ContainsValidStartCode(packet.Data.Span))
        {
            throw new InvalidOperationException("Annex-B H.264 packet does not contain a valid start code.");
        }
    }

    private void CaptureCodecConfiguration(EncodedVideoPacket packet)
    {
        if (!packet.CodecConfiguration.IsEmpty)
        {
            var configuration = packet.CodecConfiguration.ToArray();
            if (_avcC.Length > 0 && !_avcC.AsSpan().SequenceEqual(configuration))
                throw new NotSupportedException("MP4 recording does not support changing H.264 codec configuration.");

            _avcC = configuration;
            return;
        }

        foreach (var nal in H264NalUtilities.ExtractAnnexBNalUnits(packet.Data.Span))
        {
            if (nal.Length == 0)
                continue;

            var nalType = nal[0] & 0x1F;
            if (nalType == 7 && _sps.Length == 0)
                _sps = nal;
            else if (nalType == 8 && _pps.Length == 0)
                _pps = nal;
        }
    }

    private ReadOnlyMemory<byte> ResolveAvcC()
    {
        if (_avcC.Length > 0)
            return _avcC;

        if (_sps.Length == 0 || _pps.Length == 0)
            throw new InvalidOperationException("Cannot finalize MP4 without H.264 SPS and PPS data.");

        if (_sps.Length < 4)
            throw new InvalidOperationException("H.264 SPS is too short to build MP4 avcC data.");

        using var content = new MemoryStream();
        content.WriteByte(0x01);
        content.Write(_sps.AsSpan(1, Math.Min(3, _sps.Length - 1)));
        content.WriteByte(0xFF);
        content.WriteByte(0xE1);
        WriteUInt16(content, (ushort)_sps.Length);
        content.Write(_sps);
        content.WriteByte(0x01);
        WriteUInt16(content, (ushort)_pps.Length);
        content.Write(_pps);
        return content.ToArray();
    }

    private static uint ResolveSampleDurationMilliseconds(EncodedVideoPacket packet) =>
        packet.Duration > TimeSpan.Zero
            ? (uint)Math.Max(1, (int)Math.Round(packet.Duration.TotalMilliseconds, MidpointRounding.AwayFromZero))
            : 1_000u / 30u;

    private static void WriteUInt16(System.IO.Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
