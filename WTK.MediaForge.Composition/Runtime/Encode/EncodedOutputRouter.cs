using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Diagnostics;
using System.Threading.Channels;

namespace WTK.MediaForge.Composition.Runtime.Encode;

internal enum EncodedPacketConsumerBackpressurePolicy
{
    DropOutput,
    FailOutput,
    KeepLatest,
    Backpressure
}

internal sealed class EncodedPacketConsumerOptions
{
    public EncodedPacketConsumerBackpressurePolicy BackpressurePolicy { get; init; } =
        EncodedPacketConsumerBackpressurePolicy.FailOutput;

    public bool RequiresLosslessDelivery { get; init; }

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public string? DisplayName { get; init; }
}

internal sealed record EncodedPacketConsumerStatistics(
    string DisplayName,
    EncodedPacketConsumerBackpressurePolicy BackpressurePolicy,
    long EnqueuedPackets,
    long WrittenPackets,
    long DroppedPackets,
    long FailedWrites,
    long TimedOutWrites,
    string? LastError);

/// <summary>
/// Routes encoded packets from a single hardware encoder to multiple output sinks.
/// </summary>
internal sealed class EncodedOutputRouter : IAsyncDisposable
{
    private readonly IHardwareVideoEncoder _encoder;
    private readonly List<EncodedPacketConsumerWorker> _consumers = [];
    private readonly int _consumerQueueCapacity;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private bool _disposed;

    public EncodedOutputRouter(
        IHardwareVideoEncoder encoder,
        int consumerQueueCapacity = 8,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        if (consumerQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(consumerQueueCapacity), "Consumer queue capacity must be positive.");

        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
        _consumerQueueCapacity = consumerQueueCapacity;
        _diagnostics = diagnostics;
    }

    public IHardwareVideoEncoder Encoder => _encoder;

    public IReadOnlyList<EncodedPacketConsumerStatistics> GetConsumerStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _consumers.Select(static consumer => consumer.GetStatistics()).ToArray();
    }

    public EncodedPacketConsumerWorker RegisterConsumer(
        IEncodedPacketConsumer consumer,
        EncodedPacketConsumerOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(consumer);

        var existing = _consumers.FirstOrDefault(worker => ReferenceEquals(worker.Consumer, consumer));
        if (existing is not null)
            return existing;

        var worker = new EncodedPacketConsumerWorker(
            consumer,
            _consumerQueueCapacity,
            options ?? new EncodedPacketConsumerOptions(),
            _diagnostics);
        _consumers.Add(worker);
        return worker;
    }

    public void RoutePacket(EncodedVideoPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);

        foreach (var consumer in _consumers)
        {
            try
            {
                consumer.Enqueue(packet);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encoded_router_consumer_enqueue_failed",
                    $"Encoded packet consumer '{consumer.DisplayName}' failed while accepting a packet.",
                    nameof(EncodedOutputRouter),
                    ex);
            }
        }
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

        List<Exception>? failures = null;
        foreach (var consumer in _consumers)
        {
            try
            {
                await consumer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "engine.encoded_router_consumer_flush_failed",
                    $"Encoded packet consumer '{consumer.DisplayName}' failed while flushing.",
                    nameof(EncodedOutputRouter),
                    ex);
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("One or more encoded packet consumers failed while flushing.", failures);
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
    private readonly EncodedPacketConsumerOptions _options;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private Exception? _failure;
    private string? _lastError;
    private long _enqueuedPackets;
    private long _writtenPackets;
    private long _droppedPackets;
    private long _failedWrites;
    private long _timedOutWrites;

    public EncodedPacketConsumerWorker(
        IEncodedPacketConsumer consumer,
        int capacity,
        EncodedPacketConsumerOptions options,
        IMediaForgeDiagnosticsSink? diagnostics)
    {
        Consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.WriteTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Consumer write timeout must be positive.");

        if (_options.RequiresLosslessDelivery &&
            _options.BackpressurePolicy is
                EncodedPacketConsumerBackpressurePolicy.DropOutput or
                EncodedPacketConsumerBackpressurePolicy.KeepLatest)
        {
            throw new ArgumentException(
                "Lossless encoded outputs cannot use dropping backpressure policies.",
                nameof(options));
        }

        _diagnostics = diagnostics;
        _queue = Channel.CreateBounded<EncodedPacketConsumerWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = ToChannelFullMode(_options.BackpressurePolicy),
            AllowSynchronousContinuations = false
        });
        _worker = Task.Run(ProcessAsync);
    }

    public IEncodedPacketConsumer Consumer { get; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(_options.DisplayName)
            ? Consumer.GetType().Name
            : _options.DisplayName!;

    public long DroppedPackets => Volatile.Read(ref _droppedPackets);

    public EncodedPacketConsumerStatistics GetStatistics() =>
        new(
            DisplayName,
            _options.BackpressurePolicy,
            Volatile.Read(ref _enqueuedPackets),
            Volatile.Read(ref _writtenPackets),
            Volatile.Read(ref _droppedPackets),
            Volatile.Read(ref _failedWrites),
            Volatile.Read(ref _timedOutWrites),
            Volatile.Read(ref _lastError));

    public void Enqueue(EncodedVideoPacket packet)
    {
        if (_failure is not null)
            throw new InvalidOperationException("Encoded output consumer has failed.", _failure);

        if (_queue.Writer.TryWrite(EncodedPacketConsumerWorkItem.FromPacket(packet)))
        {
            Interlocked.Increment(ref _enqueuedPackets);
            return;
        }

        if (_options.BackpressurePolicy is
            EncodedPacketConsumerBackpressurePolicy.DropOutput or
            EncodedPacketConsumerBackpressurePolicy.KeepLatest)
        {
            Interlocked.Increment(ref _droppedPackets);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Warning,
                "engine.encoded_router_consumer_packet_dropped",
                $"Encoded packet consumer '{DisplayName}' dropped a packet because its queue is full.",
                nameof(EncodedPacketConsumerWorker));
            return;
        }

        var exception = new InvalidOperationException(
            "Encoded output consumer queue is full; sink backpressure must be handled explicitly.");
        if (_options.BackpressurePolicy == EncodedPacketConsumerBackpressurePolicy.FailOutput)
        {
            _failure = exception;
            Volatile.Write(ref _lastError, exception.Message);
        }

        throw exception;
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

                using var timeout = new CancellationTokenSource(_options.WriteTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, timeout.Token);
                try
                {
                    await Consumer.WriteEncodedPacketAsync(item.Packet!, linked.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref _writtenPackets);
                }
                catch (OperationCanceledException ex) when (
                    timeout.IsCancellationRequested &&
                    !_stop.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _timedOutWrites);
                    throw new TimeoutException(
                        $"Encoded packet consumer '{DisplayName}' did not complete a write within {_options.WriteTimeout}.",
                        ex);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _failure = ex;
            Volatile.Write(ref _lastError, ex.Message);
            Interlocked.Increment(ref _failedWrites);
            while (_queue.Reader.TryRead(out var item))
                item.FlushCompletion?.TrySetException(ex);

            _queue.Writer.TryComplete(ex);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "engine.encoded_router_consumer_failed",
                $"Encoded packet consumer '{DisplayName}' failed.",
                nameof(EncodedPacketConsumerWorker),
                ex);
        }
    }

    private static BoundedChannelFullMode ToChannelFullMode(EncodedPacketConsumerBackpressurePolicy policy) =>
        policy switch
        {
            EncodedPacketConsumerBackpressurePolicy.DropOutput => BoundedChannelFullMode.Wait,
            EncodedPacketConsumerBackpressurePolicy.KeepLatest => BoundedChannelFullMode.DropOldest,
            EncodedPacketConsumerBackpressurePolicy.Backpressure => BoundedChannelFullMode.Wait,
            EncodedPacketConsumerBackpressurePolicy.FailOutput => BoundedChannelFullMode.Wait,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported encoded consumer policy.")
        };
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
