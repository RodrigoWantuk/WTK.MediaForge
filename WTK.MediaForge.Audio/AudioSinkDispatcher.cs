using System.Threading;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Audio;

public sealed record AudioRouteDispatchResult(int MatchedRoutes, int QueuedRoutes, int DroppedRoutes);

public sealed record AudioSinkDispatcherHealth(int QueuedBlocks, long DroppedBlocks, int HighWaterMark)
{
    public static AudioSinkDispatcherHealth Empty { get; } = new(0, 0, 0);
}

/// <summary>
/// Bounded route queues for the portable Program Mix. One audio callback thread
/// produces leases and one non-real-time consumer drains each route queue.
/// </summary>
public sealed class AudioSinkDispatcher
{
    private readonly Dictionary<AudioOutputRouteId, AudioRouteQueue> _routes;
    private readonly Dictionary<AudioBusId, AudioRouteQueue[]> _routesByBus;

    public AudioSinkDispatcher(AudioGraphDefinition graph, int queueCapacity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity));

        var sinks = graph.Sinks.ToDictionary(static sink => sink.Id);
        _routes = new Dictionary<AudioOutputRouteId, AudioRouteQueue>();
        var byBus = new Dictionary<AudioBusId, List<AudioRouteQueue>>();
        foreach (var route in graph.OutputRoutes.Where(static route => route.Enabled))
        {
            var sink = sinks[route.SinkId];
            if (!sink.Enabled || sink.Kind != AudioSinkKind.ProgramMix)
                continue;
            var queue = new AudioRouteQueue(route.Id, route.BusId, queueCapacity);
            _routes.Add(route.Id, queue);
            if (!byBus.TryGetValue(route.BusId, out var routes))
            {
                routes = [];
                byBus.Add(route.BusId, routes);
            }
            routes.Add(queue);
        }
        _routesByBus = byBus.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray());
    }

    public AudioRouteDispatchResult Dispatch(
        AudioBusId busId,
        AudioBlock busBlock,
        AudioBufferPool bufferPool,
        AudioQuantum quantum)
    {
        ArgumentNullException.ThrowIfNull(busBlock);
        ArgumentNullException.ThrowIfNull(bufferPool);
        if (!_routesByBus.TryGetValue(busId, out var routes))
            return new AudioRouteDispatchResult(0, 0, 0);

        var queued = 0;
        var dropped = 0;
        foreach (var route in routes)
        {
            var copy = bufferPool.RentPrepared(
                busBlock.Format,
                quantum,
                busBlock.Timestamp,
                busBlock.Sequence,
                busBlock.Flags);
            Copy(busBlock, copy.Block);
            if (route.TryEnqueue(copy))
            {
                queued++;
                continue;
            }

            copy.Dispose();
            route.RecordDrop();
            dropped++;
        }
        return new AudioRouteDispatchResult(routes.Length, queued, dropped);
    }

    public bool TryDequeue(AudioOutputRouteId routeId, out AudioBlockLease? lease)
    {
        if (_routes.TryGetValue(routeId, out var route) && route.TryDequeue(out lease))
            return true;

        lease = null;
        return false;
    }

    public AudioSinkDispatcherHealth GetHealth()
    {
        var queued = 0;
        var dropped = 0L;
        var highWater = 0;
        foreach (var route in _routes.Values)
        {
            queued += route.QueuedBlocks;
            dropped += route.DroppedBlocks;
            highWater = Math.Max(highWater, route.HighWaterMark);
        }
        return new AudioSinkDispatcherHealth(queued, dropped, highWater);
    }

    public void Drain()
    {
        foreach (var route in _routes.Values)
            route.Drain();
    }

    private static void Copy(AudioBlock source, AudioBlock destination)
    {
        for (var channel = 0; channel < source.Channels.Length; channel++)
            Array.Copy(source.Channels[channel], destination.Channels[channel], source.Frames);
    }

    private sealed class AudioRouteQueue(AudioOutputRouteId id, AudioBusId busId, int capacity)
    {
        private readonly AudioBlockLease?[] _slots = new AudioBlockLease[capacity];
        private long _head;
        private long _tail;
        private long _droppedBlocks;
        private int _highWaterMark;

        public AudioOutputRouteId Id { get; } = id;
        public AudioBusId BusId { get; } = busId;
        public int QueuedBlocks => checked((int)Math.Max(0, Volatile.Read(ref _tail) - Volatile.Read(ref _head)));
        public long DroppedBlocks => Volatile.Read(ref _droppedBlocks);
        public int HighWaterMark => Volatile.Read(ref _highWaterMark);

        public bool TryEnqueue(AudioBlockLease lease)
        {
            var tail = Volatile.Read(ref _tail);
            if (tail - Volatile.Read(ref _head) >= _slots.Length)
                return false;
            var index = (int)(tail % _slots.Length);
            if (Interlocked.CompareExchange(ref _slots[index], lease, null) is not null)
                return false;
            Volatile.Write(ref _tail, tail + 1);
            UpdateHighWaterMark(QueuedBlocks);
            return true;
        }

        public bool TryDequeue(out AudioBlockLease? lease)
        {
            var head = Volatile.Read(ref _head);
            if (head >= Volatile.Read(ref _tail))
            {
                lease = null;
                return false;
            }
            var index = (int)(head % _slots.Length);
            lease = Interlocked.Exchange(ref _slots[index], null);
            if (lease is null)
                return false;
            Volatile.Write(ref _head, head + 1);
            return true;
        }

        public void RecordDrop() => Interlocked.Increment(ref _droppedBlocks);

        public void Drain()
        {
            while (TryDequeue(out var lease))
                lease!.Dispose();
        }

        private void UpdateHighWaterMark(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _highWaterMark);
                if (value <= current || Interlocked.CompareExchange(ref _highWaterMark, value, current) == current)
                    return;
            }
        }
    }
}
