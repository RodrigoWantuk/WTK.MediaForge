using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Media.Mux;

internal sealed class EncodedPacketMp4Muxer : IMp4Muxer
{
    private readonly string _outputPath;
    private readonly string _temporaryOutputPath;
    private readonly string _payloadPath;
    private readonly FileStream _payloadStream;
    private readonly IsoBmffMp4Writer.TrackMetadata _track;
    private readonly List<IsoBmffMp4Writer.SampleMetadata> _samples = [];
    private byte[] _avcC = [];
    private byte[] _sps = [];
    private byte[] _pps = [];
    private EncodedVideoBitstreamFormat? _bitstreamFormat;
    private bool _finalized;
    private bool _disposed;
    private bool _payloadClosed;

    public EncodedPacketMp4Muxer(string outputPath, uint width, uint height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (width == 0)
            throw new ArgumentOutOfRangeException(nameof(width), "MP4 track width must be positive.");

        if (height == 0)
            throw new ArgumentOutOfRangeException(nameof(height), "MP4 track height must be positive.");

        _track = new IsoBmffMp4Writer.TrackMetadata(width, height);
        _outputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(_outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            outputDirectory = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(outputDirectory);
        var token = Guid.NewGuid().ToString("N");
        _temporaryOutputPath = Path.Combine(outputDirectory, $".{Path.GetFileName(_outputPath)}.{token}.tmp");
        _payloadPath = Path.Combine(outputDirectory, $".{Path.GetFileName(_outputPath)}.{token}.payload");
        _payloadStream = new FileStream(
            _payloadPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024);
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

        uint sampleSize;
        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB)
        {
            sampleSize = IsoBmffMp4Writer.WriteAnnexBAsAvccSample(_payloadStream, packet.Data.Span);
        }
        else
        {
            sampleSize = IsoBmffMp4Writer.WriteAvccSample(_payloadStream, packet.Data.Span);
        }

        _samples.Add(new IsoBmffMp4Writer.SampleMetadata(
            ResolveSampleDurationMilliseconds(packet),
            sampleSize,
            packet.IsKeyFrame || (H264NalUtilities.TryGetFirstNalType(packet.Data.Span, out var nalType) && nalType == 5)));

        return ValueTask.CompletedTask;
    }

    public ValueTask FinalizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
            return ValueTask.CompletedTask;

        if (_samples.Count == 0)
            throw new InvalidOperationException("Cannot finalize MP4 without encoded packets.");

        var avcC = ResolveAvcC();
        if (!_payloadClosed)
        {
            _payloadStream.Flush(flushToDisk: true);
            _payloadStream.Dispose();
            _payloadClosed = true;
        }

        try
        {
            if (File.Exists(_temporaryOutputPath))
                File.Delete(_temporaryOutputPath);

            IsoBmffMp4Writer.WriteMp4FromAvccSamples(
                _temporaryOutputPath,
                _payloadPath,
                _samples,
                avcC,
                _track);

            if (!IsoBmffMp4Writer.HasValidH264BoxStructure(
                _temporaryOutputPath,
                _track,
                _samples.Count))
            {
                throw new InvalidOperationException("MP4 muxer wrote an invalid H.264 box structure.");
            }

            File.Move(_temporaryOutputPath, _outputPath, overwrite: true);
            _finalized = true;
        }
        catch
        {
            if (File.Exists(_temporaryOutputPath))
                File.Delete(_temporaryOutputPath);

            throw;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        if (!_payloadClosed)
            _payloadStream.Dispose();

        if (File.Exists(_payloadPath))
            File.Delete(_payloadPath);

        if (File.Exists(_temporaryOutputPath))
            File.Delete(_temporaryOutputPath);

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

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.AnnexB &&
            !H264NalUtilities.ContainsAnnexBNalPayload(packet.Data.Span))
        {
            throw new InvalidOperationException("Annex-B H.264 packet does not contain a NAL payload.");
        }
    }

    private void CaptureCodecConfiguration(EncodedVideoPacket packet)
    {
        if (!packet.CodecConfiguration.IsEmpty)
        {
            if (!IsoBmffMp4Writer.TryNormalizeH264CodecConfiguration(
                    packet.CodecConfiguration.Span,
                    out var configuration))
            {
                return;
            }

            if (_avcC.Length > 0 && !_avcC.AsSpan().SequenceEqual(configuration))
                throw new NotSupportedException("MP4 recording does not support changing H.264 codec configuration.");

            _avcC = configuration;
            return;
        }

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
            if (nalType == 7 && _sps.Length == 0)
                _sps = nal.ToArray();
            else if (nalType == 8 && _pps.Length == 0)
                _pps = nal.ToArray();
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
