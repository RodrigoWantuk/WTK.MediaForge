using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// RTMP packet sink using hardware-encoded H.264 packets only.
/// </summary>
public sealed class RtmpPacketSink : IEncodedPacketSink
{
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(5);

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
            using var timeout = new CancellationTokenSource(DefaultOperationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await _transport.ConnectAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (
                timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"RTMP connect/publish did not complete within {DefaultOperationTimeout}.", ex);
            }

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

        if (!_allowPrototypeTransport &&
            packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
        {
            throw new NotSupportedException(
                "Product RTMP output requires packets with BackendOutputValidated evidence.");
        }

        var flvPackets = _packetizer.Packetize(packet, includeCodecConfiguration: !_codecConfigurationSent);
        foreach (var flvPacket in flvPackets)
        {
            using var timeout = new CancellationTokenSource(DefaultOperationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await _transport.SendAsync(flvPacket, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (
                timeout.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"RTMP packet write did not complete within {DefaultOperationTimeout}.", ex);
            }

            _codecConfigurationSent |= flvPacket.IsCodecConfiguration;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        _codecConfigurationSent = false;
        _transport?.Dispose();
        _transport = null;
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
