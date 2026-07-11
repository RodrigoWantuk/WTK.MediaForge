using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// Hardware-encoded MP4 recording packet sink.
/// FFmpeg and libx264 are prohibited on this path.
/// </summary>
public sealed class RecordingMp4PacketSink : IEncodedPacketSink
{
    private readonly string _outputPath;
    private readonly IMediaTransportAuditSink? _auditSink;
    private readonly bool _allowPrototypeMuxer;
    private IMp4Muxer? _muxer;
    private EncodedPacketSinkContext? _context;
    private bool _started;

    public RecordingMp4PacketSink(string outputPath, IMediaTransportAuditSink? auditSink = null)
        : this(outputPath, auditSink, allowPrototypeMuxer: false)
    {
    }

    internal RecordingMp4PacketSink(
        string outputPath,
        IMediaTransportAuditSink? auditSink,
        bool allowPrototypeMuxer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _auditSink = auditSink;
        _allowPrototypeMuxer = allowPrototypeMuxer;
    }

    public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_allowPrototypeMuxer)
        {
            throw new NotSupportedException(
                "RecordingMp4PacketSink is prototype-only until real hardware encoder output and production MP4 muxing are implemented.");
        }

        if (context.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"MP4 recording currently accepts H.264 packets, not '{context.Codec}'.");

        _context = context;
        _muxer = new PrototypeEncodedPacketMp4Muxer(_outputPath, _auditSink);
        _started = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started || _muxer is null)
            throw new InvalidOperationException("Recording MP4 packet sink has not been started.");

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"MP4 recording currently accepts H.264 packets, not '{packet.Codec}'.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("MP4 recording requires packets with an explicit H.264 bitstream format.");

        return _muxer.WritePacketAsync(packet, cancellationToken);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_muxer is not null)
            await _muxer.FinalizeAsync(cancellationToken).ConfigureAwait(false);

        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_muxer is not null)
            await _muxer.DisposeAsync().ConfigureAwait(false);

        _muxer = null;
        _context = null;
        _started = false;
    }
}

public sealed class RecordingMp4Sink : IEncodedPacketSink
{
    private readonly RecordingMp4PacketSink _inner;

    public RecordingMp4Sink(string outputPath, IMediaTransportAuditSink? auditSink = null)
        : this(outputPath, auditSink, allowPrototypeMuxer: false)
    {
    }

    internal RecordingMp4Sink(
        string outputPath,
        IMediaTransportAuditSink? auditSink,
        bool allowPrototypeMuxer) =>
        _inner = new RecordingMp4PacketSink(outputPath, auditSink, allowPrototypeMuxer);

    public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken) =>
        _inner.StartAsync(context, cancellationToken);

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _inner.WritePacketAsync(packet, cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        _inner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
