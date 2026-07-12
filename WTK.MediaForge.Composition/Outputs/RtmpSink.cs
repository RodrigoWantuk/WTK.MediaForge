using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// RTMP packet sink using hardware-encoded H.264 packets only.
/// </summary>
public sealed class RtmpPacketSink : IEncodedPacketSink
{
    private readonly string _url;
    private readonly bool _allowPrototypeTransport;
    private readonly FlvPacketizer _packetizer = new();
    private IRtmpTransport? _transport;
    private bool _codecConfigurationSent;
    private bool _started;

    public RtmpPacketSink(string url)
        : this(url, allowPrototypeTransport: false)
    {
    }

    internal RtmpPacketSink(string url, bool allowPrototypeTransport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _url = url;
        _allowPrototypeTransport = allowPrototypeTransport;
    }

    internal IReadOnlyList<FlvPacket> SentPacketsForTests =>
        _transport is InMemoryRtmpTransport inMemoryTransport
            ? inMemoryTransport.SentPackets
            : Array.Empty<FlvPacket>();

    public async ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"RTMP currently accepts H.264 packets, not '{context.Codec}'.");

        _transport = _allowPrototypeTransport
            ? new InMemoryRtmpTransport(_url)
            : new TcpRtmpTransport(_url);
        _codecConfigurationSent = false;
        try
        {
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        catch
        {
            _transport.Dispose();
            _transport = null;
            _started = false;
            throw;
        }
    }

    public async ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_started || _transport is null)
            throw new InvalidOperationException("RTMP packet sink has not been started.");

        if (packet.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"RTMP currently accepts H.264 packets, not '{packet.Codec}'.");

        if (packet.BitstreamFormat == EncodedVideoBitstreamFormat.Unknown)
            throw new NotSupportedException("RTMP requires packets with an explicit H.264 bitstream format.");

        var flvPackets = _packetizer.Packetize(packet, includeCodecConfiguration: !_codecConfigurationSent);
        foreach (var flvPacket in flvPackets)
        {
            await _transport.SendAsync(flvPacket, cancellationToken).ConfigureAwait(false);
            _codecConfigurationSent |= flvPacket.IsCodecConfiguration;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _transport?.Dispose();
        _transport = null;
        _started = false;
        _codecConfigurationSent = false;
        return ValueTask.CompletedTask;
    }
}

public sealed class RtmpSink : IEncodedPacketSink
{
    private readonly RtmpPacketSink _inner;

    public RtmpSink(string url)
        : this(url, allowPrototypeTransport: false)
    {
    }

    internal RtmpSink(string url, bool allowPrototypeTransport) =>
        _inner = new RtmpPacketSink(url, allowPrototypeTransport);

    internal IReadOnlyList<FlvPacket> SentPacketsForTests => _inner.SentPacketsForTests;

    public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken) =>
        _inner.StartAsync(context, cancellationToken);

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _inner.WritePacketAsync(packet, cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        _inner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
