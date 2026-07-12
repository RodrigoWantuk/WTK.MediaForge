using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;
using System.Threading.Channels;

namespace WTK.MediaForge.Composition.Runtime.Encode;

/// <summary>
/// Routes encoded packets from a single hardware encoder to multiple output sinks.
/// </summary>
internal sealed class EncodedOutputRouter : IAsyncDisposable
{
    private readonly IHardwareVideoEncoder _encoder;
    private readonly List<EncodedPacketConsumerWorker> _consumers = [];
    private readonly int _consumerQueueCapacity;
    private bool _disposed;

    public EncodedOutputRouter(IHardwareVideoEncoder encoder, int consumerQueueCapacity = 8)
    {
        if (consumerQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(consumerQueueCapacity), "Consumer queue capacity must be positive.");

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _consumerQueueCapacity = consumerQueueCapacity;
    }

    public IHardwareVideoEncoder Encoder => _encoder;

    public void RegisterConsumer(IEncodedPacketConsumer consumer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(consumer);

        if (_consumers.Any(worker => ReferenceEquals(worker.Consumer, consumer)))
            return;

        _consumers.Add(new EncodedPacketConsumerWorker(consumer, _consumerQueueCapacity));
    }

    public void RoutePacket(EncodedVideoPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);

        foreach (var consumer in _consumers)
            consumer.Enqueue(packet);
    }

    public ValueTask RoutePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RoutePacket(packet);
        return ValueTask.CompletedTask;
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var consumer in _consumers)
            await consumer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var consumer in _consumers)
            consumer.Complete();

        foreach (var consumer in _consumers)
            await consumer.DisposeAsync().ConfigureAwait(false);

        _consumers.Clear();
        await _encoder.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class EncodedPacketConsumerWorker : IAsyncDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<EncodedPacketConsumerWorkItem> _queue;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private Exception? _failure;

    public EncodedPacketConsumerWorker(IEncodedPacketConsumer consumer, int capacity)
    {
        Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _queue = Channel.CreateBounded<EncodedPacketConsumerWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessAsync);
    }

    public IEncodedPacketConsumer Consumer { get; }

    public void Enqueue(EncodedVideoPacket packet)
    {
        if (_failure is not null)
            throw new InvalidOperationException("Encoded output consumer has failed.", _failure);

        if (!_queue.Writer.TryWrite(EncodedPacketConsumerWorkItem.FromPacket(packet)))
        {
            throw new InvalidOperationException(
                "Encoded output consumer queue is full; sink backpressure must be handled explicitly.");
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_failure is not null)
            throw new InvalidOperationException("Encoded output consumer has failed.", _failure);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(EncodedPacketConsumerWorkItem.Flush(completion)))
        {
            throw new InvalidOperationException(
                "Encoded output consumer queue is full; cannot flush until backpressure is handled.");
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Complete() => _queue.Writer.TryComplete();

    public async ValueTask DisposeAsync()
    {
        Complete();
        try
        {
            await _worker.WaitAsync(DisposeTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _stop.Cancel();
            await _worker.WaitAsync(DisposeTimeout).ConfigureAwait(false);
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (item.FlushCompletion is not null)
                {
                    item.FlushCompletion.TrySetResult();
                    continue;
                }

                await Consumer.WriteEncodedPacketAsync(item.Packet!, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _failure = ex;
            while (_queue.Reader.TryRead(out var item))
                item.FlushCompletion?.TrySetException(ex);

            _queue.Writer.TryComplete(ex);
        }
    }
}

internal readonly record struct EncodedPacketConsumerWorkItem(
    EncodedVideoPacket? Packet,
    TaskCompletionSource? FlushCompletion)
{
    public static EncodedPacketConsumerWorkItem FromPacket(EncodedVideoPacket packet) =>
        new(packet, null);

    public static EncodedPacketConsumerWorkItem Flush(TaskCompletionSource completion) =>
        new(null, completion);
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

internal sealed class EncodedPacketSinkConsumer : IEncodedPacketConsumer
{
    private readonly IEncodedPacketSink _sink;

    public EncodedPacketSinkConsumer(IEncodedPacketSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public ValueTask WriteEncodedPacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken) =>
        _sink.WritePacketAsync(packet, cancellationToken);
}
