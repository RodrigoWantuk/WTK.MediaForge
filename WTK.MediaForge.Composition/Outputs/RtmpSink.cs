using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// Experimental RTMP sink using hardware-encoded H.264 packets only.
/// </summary>
public sealed class RtmpSink : IRenderOutputSink
{
    private readonly string _url;
    private readonly FlvPacketizer _packetizer = new();
    private RtmpTransport? _transport;
    private bool _started;

    public RtmpSink(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _url = url;
    }

    public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Streaming;

    public RenderOutputSinkBackpressureMode BackpressureMode =>
        RenderOutputSinkBackpressureMode.KeepLatest;

    public IReadOnlyList<FlvPacket> SentPacketsForTests => _transport?.SentPackets ?? Array.Empty<FlvPacket>();

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _transport = new RtmpTransport(_url);
        _started = true;
        return _transport.ConnectAsync(cancellationToken);
    }

    public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started)
            throw new InvalidOperationException("RTMP sink has not been started.");

        throw new NotSupportedException(
            "RtmpSink accepts encoded packets only. Wire hardware encoder output instead of GPU surface frames.");
    }

    public async ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        if (_transport is null)
            throw new InvalidOperationException("RTMP sink has not been started.");

        var flvPacket = _packetizer.Packetize(packet);
        await _transport.SendAsync(flvPacket, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _started = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _transport?.Dispose();
        _transport = null;
        _started = false;
        return ValueTask.CompletedTask;
    }
}
