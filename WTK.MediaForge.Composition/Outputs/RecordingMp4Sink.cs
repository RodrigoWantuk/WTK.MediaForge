using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// Hardware-encoded MP4 recording sink. Accepts encoded packets only.
/// FFmpeg and libx264 are prohibited on this path.
/// </summary>
public sealed class RecordingMp4Sink : IRenderOutputSink
{
    private readonly string _outputPath;
    private readonly IMediaTransportAuditSink? _auditSink;
    private readonly bool _allowPrototypeMuxer;
    private IMp4Muxer? _muxer;
    private RenderOutputSinkContext? _context;
    private bool _started;

    public RecordingMp4Sink(string outputPath, IMediaTransportAuditSink? auditSink = null)
        : this(outputPath, auditSink, allowPrototypeMuxer: false)
    {
    }

    internal RecordingMp4Sink(
        string outputPath,
        IMediaTransportAuditSink? auditSink,
        bool allowPrototypeMuxer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _auditSink = auditSink;
        _allowPrototypeMuxer = allowPrototypeMuxer;
    }

    public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.File;

    public RenderOutputSinkBackpressureMode BackpressureMode =>
        RenderOutputSinkBackpressureMode.KeepLatest;

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_allowPrototypeMuxer)
        {
            throw new NotSupportedException(
                "RecordingMp4Sink is prototype-only until real hardware encoder output and production MP4 muxing are implemented.");
        }

        _context = context;
        _muxer = new PrototypeEncodedPacketMp4Muxer(_outputPath, _auditSink);
        _started = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started || _muxer is null)
            throw new InvalidOperationException("Recording MP4 sink has not been started.");

        throw new NotSupportedException(
            "RecordingMp4Sink accepts encoded packets only. Wire MediaFoundationHardwareVideoEncoder output instead of GPU surface frames.");
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

    public ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        if (_muxer is null)
            throw new InvalidOperationException("Recording MP4 sink has not been started.");

        return _muxer.WritePacketAsync(packet, cancellationToken);
    }
}
