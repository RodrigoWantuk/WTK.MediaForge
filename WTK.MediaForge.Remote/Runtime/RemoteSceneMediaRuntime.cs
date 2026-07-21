using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;
using WTK.MediaForge.Core.Media.Decode;

namespace WTK.MediaForge.Remote;

public interface IRemoteScenePublisherFactory
{
    ValueTask<IRemoteScenePublisher> CreateAsync(
        RemoteSceneOutputSettings settings,
        EncodedPacketSinkContext context,
        CancellationToken cancellationToken);
}

public sealed class RemoteScenePacketSink(
    RemoteSceneOutputSettings settings,
    IRemoteScenePublisherFactory publisherFactory) : IEncodedPacketSink
{
    private readonly RemoteSceneOutputSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IRemoteScenePublisherFactory _publisherFactory = publisherFactory ?? throw new ArgumentNullException(nameof(publisherFactory));
    private IRemoteScenePublisher? _publisher;
    private int _started;

    public event EventHandler? KeyFrameRequested;

    public async ValueTask StartAsync(EncodedPacketSinkContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("Remote Scene packet sink is already started.");
        try
        {
            _publisher = await _publisherFactory.CreateAsync(_settings, context, cancellationToken).ConfigureAwait(false);
            _publisher.KeyFrameRequested += OnKeyFrameRequested;
        }
        catch
        {
            Volatile.Write(ref _started, 0);
            throw;
        }
    }

    public async ValueTask WritePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.EvidenceKind != MediaTransportAuditEvidenceKind.BackendOutputValidated)
            throw new InvalidOperationException("Remote Scene output requires packets with BackendOutputValidated evidence.");
        var publisher = _publisher ?? throw new InvalidOperationException("Remote Scene packet sink is not started.");
        var lease = EncodedVideoPacketLease.Create(packet);
        try
        {
            await publisher.SendVideoPacketAsync(lease, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ownership transferred at the call boundary. The publisher remains responsible
            // for disposal even when native send rejects the packet.
            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        var publisher = Interlocked.Exchange(ref _publisher, null);
        if (publisher is null)
            return;
        publisher.KeyFrameRequested -= OnKeyFrameRequested;
        await publisher.DisposeAsync().ConfigureAwait(false);
        Volatile.Write(ref _started, 0);
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private void OnKeyFrameRequested(object? sender, EventArgs args) => KeyFrameRequested?.Invoke(this, EventArgs.Empty);
}

public enum RemoteSceneInterruptionPolicy
{
    FreezeLastFrame,
    Placeholder
}

public sealed record RemoteSceneDecodeOptions(
    int JitterCapacity = 32,
    int TargetBufferedPackets = 3,
    RemoteSceneInterruptionPolicy InterruptionPolicy = RemoteSceneInterruptionPolicy.FreezeLastFrame);

public sealed class RemoteSceneJitterBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _targetDepth;
    private readonly SortedDictionary<(long Ticks, long Sequence), EncodedVideoPacketLease> _packets = [];
    private long _sequence;
    private int _disposed;

    public RemoteSceneJitterBuffer(int capacity, int targetDepth)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (targetDepth <= 0 || targetDepth > capacity) throw new ArgumentOutOfRangeException(nameof(targetDepth));
        _capacity = capacity;
        _targetDepth = targetDepth;
    }

    public long DroppedPackets { get; private set; }

    public void Enqueue(EncodedVideoPacketLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_gate)
        {
            if (_packets.Count >= _capacity)
            {
                if (!lease.Packet.IsKeyFrame)
                {
                    DroppedPackets++;
                    lease.Dispose();
                    return;
                }

                var delta = _packets.FirstOrDefault(item => !item.Value.Packet.IsKeyFrame);
                if (delta.Value is not null)
                {
                    _packets.Remove(delta.Key);
                    delta.Value.Dispose();
                    DroppedPackets++;
                }
                else
                {
                    var oldest = _packets.First();
                    _packets.Remove(oldest.Key);
                    oldest.Value.Dispose();
                    DroppedPackets++;
                }
            }
            _packets.Add((lease.Packet.PresentationTime.Ticks, _sequence++), lease);
        }
    }

    public bool TryDequeue(bool draining, out EncodedVideoPacketLease? lease)
    {
        lock (_gate)
        {
            if (_packets.Count == 0 || (!draining && _packets.Count < _targetDepth))
            {
                lease = null;
                return false;
            }
            var first = _packets.First();
            _packets.Remove(first.Key);
            lease = first.Value;
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        lock (_gate)
        {
            foreach (var lease in _packets.Values)
                lease.Dispose();
            _packets.Clear();
        }
    }
}

public sealed class RemoteSceneHardwareDecodePump(
    IRemoteSceneSubscriber subscriber,
    Func<IHardwareVideoDecoder> decoderFactory,
    RemoteSceneDecodeOptions? options = null)
{
    private readonly IRemoteSceneSubscriber _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
    private readonly Func<IHardwareVideoDecoder> _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
    private readonly RemoteSceneDecodeOptions _options = options ?? new RemoteSceneDecodeOptions();
    private RemoteSceneFormatChangedEventArgs? _format;
    private long _formatGeneration;

    public RemoteSceneInterruptionPolicy InterruptionPolicy => _options.InterruptionPolicy;

    public async IAsyncEnumerable<DecodedGpuFrame> DecodeAsync(
        IMediaTransportAuditSink audit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audit);
        using var jitter = new RemoteSceneJitterBuffer(_options.JitterCapacity, _options.TargetBufferedPackets);
        IHardwareVideoDecoder? decoder = null;
        long openedGeneration = -1;
        _subscriber.FormatChanged += OnFormatChanged;
        try
        {
            await foreach (var incoming in _subscriber.VideoPackets.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                jitter.Enqueue(incoming);
                while (jitter.TryDequeue(draining: false, out var lease))
                {
                    using (var ownedLease = lease!)
                    {
                        var format = Volatile.Read(ref _format)
                            ?? throw new InvalidOperationException("Remote Scene format must be negotiated before video packets.");
                        var generation = Volatile.Read(ref _formatGeneration);
                        if (decoder is null || generation != openedGeneration)
                        {
                            if (decoder is not null)
                            {
                                await decoder.FlushAsync(audit).ConfigureAwait(false);
                                await decoder.DisposeAsync().ConfigureAwait(false);
                            }
                            decoder = _decoderFactory();
                            await decoder.OpenAsync(new HardwareDecodeOpenContext
                            {
                                SourcePath = "remote-scene",
                                Session = new HardwareDecodeSession
                                {
                                    Codec = EncodedVideoCodec.H264,
                                    Width = format.Width,
                                    Height = format.Height,
                                    PreferHardware = true
                                },
                                CancellationToken = cancellationToken
                            }, audit).ConfigureAwait(false);
                            if (!decoder.Info.ProducesGpuSurface)
                                throw new NotSupportedException("Remote Scene decoder must produce GPU surfaces.");
                            openedGeneration = generation;
                        }

                        var frame = await decoder.DecodeAsync(new DecodeFrameContext
                        {
                            Packet = ownedLease.Packet,
                            PresentationTime = ownedLease.Packet.PresentationTime,
                            CancellationToken = cancellationToken
                        }, audit).ConfigureAwait(false);
                        if (frame is not null)
                            yield return frame;
                    }
                }
            }

            while (jitter.TryDequeue(draining: true, out var lease))
            {
                using (var ownedLease = lease!)
                {
                    if (decoder is null)
                        break;
                    var frame = await decoder.DecodeAsync(new DecodeFrameContext
                    {
                        Packet = ownedLease.Packet,
                        PresentationTime = ownedLease.Packet.PresentationTime,
                        CancellationToken = cancellationToken
                    }, audit).ConfigureAwait(false);
                    if (frame is not null)
                        yield return frame;
                }
            }
        }
        finally
        {
            _subscriber.FormatChanged -= OnFormatChanged;
            if (decoder is not null)
            {
                await decoder.FlushAsync(audit).ConfigureAwait(false);
                await decoder.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void OnFormatChanged(object? sender, RemoteSceneFormatChangedEventArgs args)
    {
        Volatile.Write(ref _format, args);
        Interlocked.Increment(ref _formatGeneration);
    }
}
