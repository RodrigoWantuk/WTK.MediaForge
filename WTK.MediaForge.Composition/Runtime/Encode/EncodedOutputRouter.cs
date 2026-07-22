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
    DropOldest,
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
    private readonly object _gate = new();
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
        lock (_gate)
            return _consumers.Select(static consumer => consumer.GetStatistics()).ToArray();
    }

    public EncodedPacketConsumerWorker RegisterConsumer(
        IEncodedPacketConsumer consumer,
        EncodedPacketConsumerOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(consumer);

        lock (_gate)
        {
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
    }

    public async ValueTask UnregisterConsumerAsync(
        EncodedPacketConsumerWorker worker,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(worker);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_consumers.Remove(worker))
                return;
        }

        Exception? flushFailure = null;
        try
        {
            await worker.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            flushFailure = exception;
        }

        await worker.DisposeAsync().ConfigureAwait(false);
        if (flushFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(flushFailure).Throw();
    }

    public async ValueTask RoutePacketAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        EncodedPacketConsumerWorker[] consumers;
        lock (_gate)
            consumers = _consumers.ToArray();
        var writes = consumers.Select(consumer => consumer.EnqueueAsync(packet, cancellationToken).AsTask()).ToArray();
        await Task.WhenAll(writes).ConfigureAwait(false);
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        List<Exception>? failures = null;
        EncodedPacketConsumerWorker[] consumers;
        lock (_gate)
            consumers = _consumers.ToArray();
        foreach (var consumer in consumers)
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

        EncodedPacketConsumerWorker[] consumers;
        lock (_gate)
        {
            _disposed = true;
            consumers = _consumers.ToArray();
            _consumers.Clear();
        }
        foreach (var consumer in consumers)
            consumer.Complete();

        foreach (var consumer in consumers)
            await consumer.DisposeAsync().ConfigureAwait(false);
        await _encoder.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class EncodedPacketConsumerWorker : IAsyncDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<EncodedPacketConsumerWorkItem> _queue;
    private readonly SemaphoreSlim _enqueueGate = new(1, 1);
    private readonly SemaphoreSlim _flushGate = new(1, 1);
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
    private int _flushPending;

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
                EncodedPacketConsumerBackpressurePolicy.DropOldest)
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
            FullMode = BoundedChannelFullMode.Wait,
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

    public async ValueTask EnqueueAsync(EncodedVideoPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();
        await _enqueueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_failure is not null)
            {
                if (_options.BackpressurePolicy == EncodedPacketConsumerBackpressurePolicy.DropOutput)
                {
                    Interlocked.Increment(ref _droppedPackets);
                    return;
                }
                throw new InvalidOperationException("Encoded output consumer has failed.", _failure);
            }

            var item = EncodedPacketConsumerWorkItem.FromPacket(packet);
            switch (_options.BackpressurePolicy)
            {
                case EncodedPacketConsumerBackpressurePolicy.Backpressure:
                    await WriteAsync(item, cancellationToken).ConfigureAwait(false);
                    return;
                case EncodedPacketConsumerBackpressurePolicy.FailOutput:
                    if (TryWrite(item))
                        return;
                    var failure = new InvalidOperationException("Encoded output consumer queue is full.");
                    _failure = failure;
                    Volatile.Write(ref _lastError, failure.Message);
                    _queue.Writer.TryComplete(failure);
                    throw failure;
                case EncodedPacketConsumerBackpressurePolicy.DropOutput:
                    if (TryWrite(item))
                        return;
                    Interlocked.Increment(ref _droppedPackets);
                    var isolation = new InvalidOperationException("Encoded output consumer was isolated because its queue is full.");
                    _failure = isolation;
                    Volatile.Write(ref _lastError, isolation.Message);
                    _queue.Writer.TryComplete();
                    ReportDroppedPacket("isolated");
                    return;
                case EncodedPacketConsumerBackpressurePolicy.DropOldest:
                    if (Volatile.Read(ref _flushPending) != 0)
                    {
                        await WriteAsync(item, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    if (TryWrite(item))
                        return;
                    if (!_queue.Reader.TryRead(out var dropped) || dropped.FlushCompletion is not null)
                        throw new InvalidOperationException("Encoded output queue could not evict its oldest packet safely.");
                    Interlocked.Increment(ref _droppedPackets);
                    ReportDroppedPacket("dropped its oldest packet");
                    if (!TryWrite(item))
                        throw new InvalidOperationException("Encoded output queue rejected a packet after evicting its oldest packet.");
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        finally
        {
            _enqueueGate.Release();
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        if (_failure is not null)
            throw new InvalidOperationException("Encoded output consumer has failed.", _failure);

        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _flushPending, 1);
            await _enqueueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_failure is not null)
                    throw new InvalidOperationException("Encoded output consumer has failed.", _failure);
                await WriteAsync(EncodedPacketConsumerWorkItem.Flush(completion), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _enqueueGate.Release();
            }

            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _flushPending, 0);
            _flushGate.Release();
        }
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

    private bool TryWrite(EncodedPacketConsumerWorkItem item)
    {
        if (!_queue.Writer.TryWrite(item))
            return false;
        if (item.Packet is not null)
            Interlocked.Increment(ref _enqueuedPackets);
        return true;
    }

    private async ValueTask WriteAsync(EncodedPacketConsumerWorkItem item, CancellationToken cancellationToken)
    {
        await _queue.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        if (item.Packet is not null)
            Interlocked.Increment(ref _enqueuedPackets);
    }

    private void ReportDroppedPacket(string action) =>
        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Warning,
            "engine.encoded_router_consumer_packet_dropped",
            $"Encoded packet consumer '{DisplayName}' {action} because its queue is full.",
            nameof(EncodedPacketConsumerWorker));
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
