using WTK.MediaForge.Composition.Media.Stream;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Composition.Outputs;

public enum RtmpOutputRuntimeStatus
{
    Stopped,
    Connecting,
    Live,
    Recovering,
    Failed
}

public sealed class RtmpOutputStatusChangedEventArgs(
    RtmpOutputRuntimeStatus status,
    string? reason) : EventArgs
{
    public RtmpOutputRuntimeStatus Status { get; } = status;

    public string? Reason { get; } = reason;
}

/// <summary>
/// RTMP packet sink using hardware-encoded H.264 packets only.
/// </summary>
public sealed class RtmpPacketSink : IEncodedPacketSink
{
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(5);

    private readonly string _url;
    private readonly Func<IRtmpTransport> _transportFactory;
    private readonly FlvPacketizer _packetizer = new();
    private IRtmpTransport? _transport;
    private bool _codecConfigurationSent;
    private bool _started;
    private long _droppedPacketsDuringRecovery;
    private RtmpOutputRuntimeStatus _status = RtmpOutputRuntimeStatus.Stopped;
    private string? _statusReason;

    public RtmpPacketSink(string url)
        : this(url, () => new TcpRtmpTransport(url))
    {
    }

    internal RtmpPacketSink(
        string url,
        Func<IRtmpTransport> transportFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        _url = url;
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public event EventHandler<RtmpOutputStatusChangedEventArgs>? StatusChanged;

    public RtmpOutputRuntimeStatus Status => _status;

    public string? StatusReason => Volatile.Read(ref _statusReason);

    public long DroppedPacketsDuringRecovery => Interlocked.Read(ref _droppedPacketsDuringRecovery);

    public async ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Codec != EncodedVideoCodec.H264)
            throw new NotSupportedException($"RTMP currently accepts H.264 packets, not '{context.Codec}'.");

        SetStatus(RtmpOutputRuntimeStatus.Connecting, null);
        _transport = _transportFactory();
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
            SetStatus(RtmpOutputRuntimeStatus.Live, null);
        }
        catch (Exception ex)
        {
            _transport.Dispose();
            _transport = null;
            _started = false;
            SetStatus(RtmpOutputRuntimeStatus.Failed, ex.Message);
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

        if (packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
        {
            throw new NotSupportedException(
                "Product RTMP output requires packets with BackendOutputValidated evidence.");
        }

        try
        {
            await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ReconnectAsync(ex, cancellationToken).ConfigureAwait(false);
            if (!packet.IsKeyFrame)
            {
                Interlocked.Increment(ref _droppedPacketsDuringRecovery);
                return;
            }

            await SendPacketAsync(packet, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask SendPacketAsync(
        EncodedVideoPacket packet,
        CancellationToken cancellationToken)
    {
        var flvPackets = _packetizer.Packetize(packet, includeCodecConfiguration: !_codecConfigurationSent);
        foreach (var flvPacket in flvPackets)
        {
            using var timeout = new CancellationTokenSource(DefaultOperationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await _transport!.SendAsync(flvPacket, linked.Token).ConfigureAwait(false);
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

    private async ValueTask ReconnectAsync(Exception failure, CancellationToken cancellationToken)
    {
        SetStatus(RtmpOutputRuntimeStatus.Recovering, failure.Message);
        _transport?.Dispose();
        _transport = null;
        _codecConfigurationSent = false;

        Exception? lastFailure = failure;
        var backoff = TimeSpan.FromMilliseconds(250);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 1)
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);

            var candidate = _transportFactory();
            try
            {
                using var timeout = new CancellationTokenSource(DefaultOperationTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                try
                {
                    await candidate.ConnectAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (
                    timeout.IsCancellationRequested &&
                    !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"RTMP reconnect attempt did not complete within {DefaultOperationTimeout}.",
                        ex);
                }
                _transport = candidate;
                SetStatus(RtmpOutputRuntimeStatus.Live, null);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                candidate.Dispose();
                lastFailure = ex;
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, 2_000));
            }
        }

        _started = false;
        SetStatus(RtmpOutputRuntimeStatus.Failed, lastFailure?.Message);
        throw new InvalidOperationException("RTMP output could not reconnect after five attempts.", lastFailure);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = false;
        _codecConfigurationSent = false;
        _transport?.Dispose();
        _transport = null;
        SetStatus(RtmpOutputRuntimeStatus.Stopped, null);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _transport?.Dispose();
        _transport = null;
        _started = false;
        _codecConfigurationSent = false;
        SetStatus(RtmpOutputRuntimeStatus.Stopped, null);
        return ValueTask.CompletedTask;
    }

    private void SetStatus(RtmpOutputRuntimeStatus status, string? reason)
    {
        _status = status;
        Volatile.Write(ref _statusReason, reason);
        try
        {
            StatusChanged?.Invoke(this, new RtmpOutputStatusChangedEventArgs(status, reason));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                $"RTMP status observer failed while handling {status}: {ex}");
        }
    }
}

public sealed class RtmpSink : IEncodedPacketSink
{
    private readonly RtmpPacketSink _inner;

    public RtmpSink(string url)
        => _inner = new RtmpPacketSink(url);

    public event EventHandler<RtmpOutputStatusChangedEventArgs>? StatusChanged
    {
        add => _inner.StatusChanged += value;
        remove => _inner.StatusChanged -= value;
    }

    public RtmpOutputRuntimeStatus Status => _inner.Status;

    public string? StatusReason => _inner.StatusReason;

    public long DroppedPacketsDuringRecovery => _inner.DroppedPacketsDuringRecovery;

    public ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken) =>
        _inner.StartAsync(context, cancellationToken);

    public ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _inner.WritePacketAsync(packet, cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        _inner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
