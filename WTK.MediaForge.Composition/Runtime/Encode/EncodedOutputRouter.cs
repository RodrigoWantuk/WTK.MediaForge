using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;

namespace WTK.MediaForge.Composition.Runtime.Encode;

/// <summary>
/// Routes encoded packets from a single hardware encoder to multiple output sinks.
/// </summary>
internal sealed class EncodedOutputRouter : IAsyncDisposable
{
    private readonly IHardwareVideoEncoder _encoder;
    private readonly List<IEncodedPacketConsumer> _consumers = [];
    private bool _disposed;

    public EncodedOutputRouter(IHardwareVideoEncoder encoder) =>
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));

    public IHardwareVideoEncoder Encoder => _encoder;

    public void RegisterConsumer(IEncodedPacketConsumer consumer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(consumer);

        if (!_consumers.Contains(consumer))
            _consumers.Add(consumer);
    }

    public async ValueTask RoutePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);

        foreach (var consumer in _consumers)
            await consumer.WriteEncodedPacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _consumers.Clear();
        return _encoder.DisposeAsync();
    }
}

internal interface IEncodedPacketConsumer
{
    ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken);
}

internal sealed class RecordingMp4PacketConsumer : IEncodedPacketConsumer
{
    private readonly IEncodedPacketSink _sink;

    public RecordingMp4PacketConsumer(IEncodedPacketSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _sink.WritePacketAsync(packet, cancellationToken);
}

internal sealed class RtmpPacketConsumer : IEncodedPacketConsumer
{
    private readonly IEncodedPacketSink _sink;

    public RtmpPacketConsumer(IEncodedPacketSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _sink.WritePacketAsync(packet, cancellationToken);
}
