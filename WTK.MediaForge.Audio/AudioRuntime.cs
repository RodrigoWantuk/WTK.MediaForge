using System.Buffers;
using System.Collections.Concurrent;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Audio;

public interface IAudioClock
{
    AudioTimestamp GetTimestamp();
}

public interface IAudioResampler
{
    void Resample(AudioBlock input, AudioBlock output);
}

public sealed class AudioBlock
{
    internal AudioBlock(AudioFormat format, int frames, float[][] channels)
    {
        Format = format;
        Frames = frames;
        Channels = channels;
    }

    public AudioFormat Format { get; }
    public int Frames { get; }
    public float[][] Channels { get; }
    public AudioTimestamp Timestamp { get; internal set; }
    public long Sequence { get; internal set; }
    public AudioBlockFlags Flags { get; internal set; }
    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / (double)Format.SampleRate);
}

public sealed class AudioBlockLease : IDisposable
{
    private AudioBufferPool? _pool;

    internal AudioBlockLease(AudioBufferPool pool, AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(pool);
        Block = block;
    }

    public AudioBlock Block { get; }

    internal void Activate(AudioBufferPool pool)
    {
        if (Interlocked.CompareExchange(ref _pool, pool, null) is not null)
            throw new InvalidOperationException("Audio block lease is already active.");
    }

    public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Return(this);
}

public sealed class AudioBufferPool
{
    private readonly ArrayPool<float> _samples;
    private readonly int _maximumRetainedBlocks;
    private readonly object _preparationGate = new();
    private readonly ConcurrentStack<AudioBlockLease> _available = new();
    private AudioFormat? _preparedFormat;
    private AudioQuantum _preparedQuantum;
    private int _rentedBlocks;
    private int _highWaterMark;

    public AudioBufferPool(int maximumRetainedBlocks = 64, ArrayPool<float>? samples = null)
    {
        if (maximumRetainedBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRetainedBlocks));
        _maximumRetainedBlocks = maximumRetainedBlocks;
        _samples = samples ?? ArrayPool<float>.Shared;
    }

    public int RentedBlocks => Volatile.Read(ref _rentedBlocks);
    public int HighWaterMark => Volatile.Read(ref _highWaterMark);
    public int PreparedBlockCount => _available.Count + RentedBlocks;

    public void Prepare(AudioFormat format, AudioQuantum quantum)
    {
        format.Validate();
        quantum.Validate();
        lock (_preparationGate)
        {
            if (_preparedFormat == format && _preparedQuantum == quantum)
                return;
            if (RentedBlocks != 0)
                throw new InvalidOperationException("Audio buffer pool cannot change format while blocks are leased.");

            while (_available.TryPop(out var retired))
                ReturnSamples(retired.Block);

            _preparedFormat = format;
            _preparedQuantum = quantum;
            for (var blockIndex = 0; blockIndex < _maximumRetainedBlocks; blockIndex++)
            {
                var channels = new float[format.ChannelCount][];
                for (var channel = 0; channel < channels.Length; channel++)
                    channels[channel] = _samples.Rent(quantum.Frames);
                _available.Push(new AudioBlockLease(this, new AudioBlock(format, quantum.Frames, channels)));
            }
        }
    }

    public AudioBlockLease Rent(AudioFormat format, AudioQuantum quantum, AudioTimestamp timestamp, long sequence, AudioBlockFlags flags = AudioBlockFlags.None)
    {
        Prepare(format, quantum);
        return RentPrepared(format, quantum, timestamp, sequence, flags);
    }

    public AudioBlockLease RentPrepared(AudioFormat format, AudioQuantum quantum, AudioTimestamp timestamp, long sequence, AudioBlockFlags flags = AudioBlockFlags.None)
    {
        if (_preparedFormat != format || _preparedQuantum != quantum)
            throw new InvalidOperationException("Audio buffer pool must be prepared before real-time processing.");
        if (!_available.TryPop(out var lease))
            throw new InvalidOperationException("Audio buffer pool is bounded and exhausted.");
        var rented = Interlocked.Increment(ref _rentedBlocks);
        UpdateHighWaterMark(rented);
        lease.Block.Timestamp = timestamp;
        lease.Block.Sequence = sequence;
        lease.Block.Flags = flags;
        lease.Activate(this);
        return lease;
    }

    internal void Return(AudioBlockLease lease)
    {
        var block = lease.Block;
        foreach (var channel in block.Channels)
            Array.Clear(channel, 0, block.Frames);
        Interlocked.Decrement(ref _rentedBlocks);
        _available.Push(lease);
    }

    private void ReturnSamples(AudioBlock block)
    {
        foreach (var channel in block.Channels)
            _samples.Return(channel);
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

public sealed record AudioRuntimeHealth(
    string Fingerprint,
    int RentedBlocks,
    int BlockHighWaterMark,
    int RetiredPlanCount,
    bool IsRunning,
    int QueuedRouteBlocks = 0,
    long DroppedRouteBlocks = 0,
    int RouteQueueHighWaterMark = 0);

public sealed class AudioRuntime
{
    private AudioPhysicalGraphPlan? _publishedPlan;
    private AudioGraphExecutionState? _executionState;
    private readonly AudioBufferPool _bufferPool;
    private readonly int _routeQueueCapacity;
    private int _running;
    private int _retiredPlanCount;

    public AudioRuntime(AudioBufferPool? bufferPool = null, int routeQueueCapacity = 3)
    {
        if (routeQueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(routeQueueCapacity));
        _bufferPool = bufferPool ?? new AudioBufferPool();
        _routeQueueCapacity = routeQueueCapacity;
    }

    public void Publish(AudioPhysicalGraphPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _bufferPool.Prepare(plan.ExecutionGraph.Format, plan.ExecutionGraph.Quantum);
        var executionState = new AudioGraphExecutionState(plan, _routeQueueCapacity);
        var previous = Interlocked.Exchange(ref _publishedPlan, plan);
        var previousState = Interlocked.Exchange(ref _executionState, executionState);
        previousState?.Reset();
        if (previous is not null)
            Interlocked.Increment(ref _retiredPlanCount);
    }

    public void Start() => Interlocked.Exchange(ref _running, 1);

    public void Stop()
    {
        Interlocked.Exchange(ref _running, 0);
        Volatile.Read(ref _executionState)?.Reset();
    }

    public AudioBlockLease ProcessSilence(AudioTimestamp timestamp, long sequence)
    {
        var plan = Volatile.Read(ref _publishedPlan) ?? throw new InvalidOperationException("An audio graph plan must be published before processing.");
        if (Volatile.Read(ref _running) == 0)
            throw new InvalidOperationException("Audio runtime is not running.");
        return _bufferPool.RentPrepared(plan.ExecutionGraph.Format, plan.ExecutionGraph.Quantum, timestamp, sequence, AudioBlockFlags.Silence);
    }

    public AudioBlockLease ProcessSource(AudioSourceId sourceId, AudioTimestamp timestamp, long sequence)
    {
        var plan = Volatile.Read(ref _publishedPlan) ?? throw new InvalidOperationException("An audio graph plan must be published before processing.");
        if (Volatile.Read(ref _running) == 0)
            throw new InvalidOperationException("Audio runtime is not running.");
        if (!plan.TryGetSource(sourceId, out var source))
            throw new InvalidOperationException("Audio source is not part of the published graph plan.");

        var flags = !source.Enabled || source.Kind == AudioSourceKind.Silence ? AudioBlockFlags.Silence : AudioBlockFlags.None;
        var lease = _bufferPool.RentPrepared(plan.ExecutionGraph.Format, plan.ExecutionGraph.Quantum, timestamp, sequence, flags);
        if (source.Enabled && source.Kind == AudioSourceKind.GeneratedTone)
            AudioSourceRenderer.RenderGeneratedTone(source, lease.Block);
        return lease;
    }

    /// <summary>
    /// Processes the published source/node DAG into one logical bus. The graph
    /// workspace is prepared during Publish; processing only rents bounded blocks
    /// and returns a caller-owned lease for the resulting mix.
    /// </summary>
    public AudioBlockLease ProcessBus(AudioBusId busId, AudioTimestamp timestamp, long sequence)
    {
        var plan = Volatile.Read(ref _publishedPlan) ?? throw new InvalidOperationException("An audio graph plan must be published before processing.");
        var state = Volatile.Read(ref _executionState) ?? throw new InvalidOperationException("An audio graph execution state must be published before processing.");
        if (Volatile.Read(ref _running) == 0)
            throw new InvalidOperationException("Audio runtime is not running.");
        if (!ReferenceEquals(state.Plan, plan))
            throw new InvalidOperationException("Audio graph plan changed while a block was being prepared.");

        return state.ProcessBus(_bufferPool, busId, timestamp, sequence);
    }

    public AudioRouteDispatchResult DispatchBus(AudioBusId busId, AudioTimestamp timestamp, long sequence)
    {
        var plan = Volatile.Read(ref _publishedPlan) ?? throw new InvalidOperationException("An audio graph plan must be published before processing.");
        var state = Volatile.Read(ref _executionState) ?? throw new InvalidOperationException("An audio graph execution state must be published before processing.");
        if (Volatile.Read(ref _running) == 0)
            throw new InvalidOperationException("Audio runtime is not running.");
        if (!ReferenceEquals(state.Plan, plan))
            throw new InvalidOperationException("Audio graph plan changed while a block was being prepared.");

        using var bus = state.ProcessBus(_bufferPool, busId, timestamp, sequence);
        return state.Dispatcher.Dispatch(busId, bus.Block, _bufferPool, plan.ExecutionGraph.Quantum);
    }

    public bool TryDequeueRoute(AudioOutputRouteId routeId, out AudioBlockLease? lease)
    {
        var state = Volatile.Read(ref _executionState);
        if (state is not null && state.Dispatcher.TryDequeue(routeId, out lease))
            return true;

        lease = null;
        return false;
    }

    public AudioRuntimeHealth GetHealth()
    {
        var plan = Volatile.Read(ref _publishedPlan);
        var dispatcherHealth = Volatile.Read(ref _executionState)?.Dispatcher.GetHealth() ?? AudioSinkDispatcherHealth.Empty;
        return new AudioRuntimeHealth(
            plan?.Fingerprint ?? string.Empty,
            _bufferPool.RentedBlocks,
            _bufferPool.HighWaterMark,
            Volatile.Read(ref _retiredPlanCount),
            Volatile.Read(ref _running) != 0,
            dispatcherHealth.QueuedBlocks,
            dispatcherHealth.DroppedBlocks,
            dispatcherHealth.HighWaterMark);
    }
}

internal sealed class AudioGraphExecutionState
{
    private readonly Dictionary<AudioSourceId, AudioBlockLease> _sourceLeases;
    private readonly Dictionary<AudioNodeId, AudioBlockLease> _nodeLeases;
    private readonly Dictionary<AudioBusId, AudioNodeId[]> _busInputs;
    private readonly AudioNodeExecution[] _nodes;
    private readonly AudioSourceDefinition[] _sources;

    public AudioGraphExecutionState(AudioPhysicalGraphPlan plan, int routeQueueCapacity)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        var graph = plan.ExecutionGraph;
        _sourceLeases = new Dictionary<AudioSourceId, AudioBlockLease>(graph.Sources.Count);
        _nodeLeases = new Dictionary<AudioNodeId, AudioBlockLease>(graph.Nodes.Count);
        _sources = graph.Sources.ToArray();

        var sourceInputs = graph.Connections
            .Where(static connection => connection.SourceId is not null)
            .GroupBy(static connection => connection.ToNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(static connection => connection.SourceId!.Value).ToArray());
        var nodeInputs = graph.Connections
            .Where(static connection => connection.FromNodeId is not null)
            .GroupBy(static connection => connection.ToNodeId)
            .ToDictionary(static group => group.Key, static group => group.Select(static connection => connection.FromNodeId!.Value).ToArray());
        var nodesById = graph.Nodes.ToDictionary(static node => node.Id);
        _nodes = plan.TopologicalNodeIds.Select(nodeId => new AudioNodeExecution(
            nodesById[nodeId],
            sourceInputs.GetValueOrDefault(nodeId, []),
            nodeInputs.GetValueOrDefault(nodeId, []),
            graph.Format,
            graph.Quantum)).ToArray();
        _busInputs = graph.Buses.ToDictionary(static bus => bus.Id, static bus => bus.InputNodeIds.ToArray());
        Dispatcher = new AudioSinkDispatcher(graph, routeQueueCapacity);
    }

    public AudioPhysicalGraphPlan Plan { get; }
    public AudioSinkDispatcher Dispatcher { get; }

    public void Reset()
    {
        foreach (var node in _nodes)
            node.Reset();
        Dispatcher.Drain();
    }

    public AudioBlockLease ProcessBus(
        AudioBufferPool bufferPool,
        AudioBusId busId,
        AudioTimestamp timestamp,
        long sequence)
    {
        if (!_busInputs.TryGetValue(busId, out var busInputs))
            throw new InvalidOperationException("Audio bus is not part of the published graph plan.");

        AudioBlockLease? result = null;
        try
        {
            foreach (var source in _sources)
            {
                var flags = !source.Enabled || source.Kind == AudioSourceKind.Silence ? AudioBlockFlags.Silence : AudioBlockFlags.None;
                var lease = bufferPool.RentPrepared(Plan.ExecutionGraph.Format, Plan.ExecutionGraph.Quantum, timestamp, sequence, flags);
                if (source.Enabled && source.Kind == AudioSourceKind.GeneratedTone)
                    AudioSourceRenderer.RenderGeneratedTone(source, lease.Block);
                _sourceLeases.Add(source.Id, lease);
            }

            foreach (var node in _nodes)
            {
                var lease = bufferPool.RentPrepared(Plan.ExecutionGraph.Format, Plan.ExecutionGraph.Quantum, timestamp, sequence);
                foreach (var sourceId in node.SourceInputs)
                    AudioBusMixer.Mix(_sourceLeases[sourceId].Block, lease.Block);
                foreach (var nodeId in node.NodeInputs)
                    AudioBusMixer.Mix(_nodeLeases[nodeId].Block, lease.Block);
                node.Apply(lease.Block);
                _nodeLeases.Add(node.Definition.Id, lease);
            }

            result = bufferPool.RentPrepared(Plan.ExecutionGraph.Format, Plan.ExecutionGraph.Quantum, timestamp, sequence);
            foreach (var nodeId in busInputs)
                AudioBusMixer.Mix(_nodeLeases[nodeId].Block, result.Block);
            return result;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
        finally
        {
            foreach (var lease in _nodeLeases.Values)
                lease.Dispose();
            foreach (var lease in _sourceLeases.Values)
                lease.Dispose();
            _nodeLeases.Clear();
            _sourceLeases.Clear();
        }
    }

    private sealed class AudioNodeExecution(
        AudioNodeDefinition definition,
        AudioSourceId[] sourceInputs,
        AudioNodeId[] nodeInputs,
        AudioFormat format,
        AudioQuantum quantum)
    {
        private readonly float[][]? _fixedDelaySamples = definition.Kind == AudioNodeKind.FixedDelay
            ? Enumerable.Range(0, format.ChannelCount).Select(_ => new float[quantum.Frames]).ToArray()
            : null;
        private bool _fixedDelayPrimed;

        public AudioNodeDefinition Definition { get; } = definition;
        public AudioSourceId[] SourceInputs { get; } = sourceInputs;
        public AudioNodeId[] NodeInputs { get; } = nodeInputs;

        public void Apply(AudioBlock block)
        {
            if (_fixedDelaySamples is not null)
            {
                for (var channel = 0; channel < block.Channels.Length; channel++)
                {
                    var current = block.Channels[channel];
                    var delayed = _fixedDelaySamples[channel];
                    for (var frame = 0; frame < block.Frames; frame++)
                    {
                        var sample = current[frame];
                        current[frame] = delayed[frame];
                        delayed[frame] = sample;
                    }
                }
                if (!_fixedDelayPrimed)
                {
                    block.Flags |= AudioBlockFlags.Discontinuity;
                    _fixedDelayPrimed = true;
                }
            }

            AudioDsp.Apply(Definition, block);
        }

        public void Reset()
        {
            if (_fixedDelaySamples is null)
                return;
            foreach (var channel in _fixedDelaySamples)
                Array.Clear(channel, 0, channel.Length);
            _fixedDelayPrimed = false;
        }
    }
}

public static class AudioBusMixer
{
    public static void Mix(AudioBlock input, AudioBlock destination)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(destination);
        if (input.Format != destination.Format || input.Frames != destination.Frames)
            throw new InvalidOperationException("Audio mixer requires matching planar formats and frame counts.");
        for (var channel = 0; channel < destination.Channels.Length; channel++)
            for (var frame = 0; frame < destination.Frames; frame++)
                destination.Channels[channel][frame] += input.Channels[channel][frame];
    }

    public static void Mix(IReadOnlyList<AudioBlock> inputs, AudioBlock destination)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var input in inputs)
            Mix(input, destination);
    }
}

public static class AudioDsp
{
    public static void Apply(AudioNodeDefinition node, AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(block);
        switch (node.Kind)
        {
            case AudioNodeKind.Gain:
                Transform(block, sample => sample * node.Value);
                break;
            case AudioNodeKind.Mute:
                if (node.Value >= 0.5f)
                    Transform(block, static _ => 0f);
                break;
            case AudioNodeKind.Polarity:
                if (node.Value >= 0.5f)
                    Transform(block, static sample => -sample);
                break;
            case AudioNodeKind.Pan when block.Format.ChannelLayout == AudioChannelLayout.Stereo:
                var pan = Math.Clamp(node.Value, -1f, 1f);
                var left = MathF.Sqrt((1f - pan) * .5f);
                var right = MathF.Sqrt((1f + pan) * .5f);
                for (var frame = 0; frame < block.Frames; frame++)
                {
                    block.Channels[0][frame] *= left;
                    block.Channels[1][frame] *= right;
                }
                break;
        }
    }

    public static (float Peak, float Rms) Measure(AudioBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var peak = 0f;
        var sum = 0d;
        var count = 0;
        foreach (var channel in block.Channels)
            for (var frame = 0; frame < block.Frames; frame++)
            {
                var sample = channel[frame];
                peak = Math.Max(peak, Math.Abs(sample));
                sum += sample * sample;
                count++;
            }
        return (peak, count == 0 ? 0f : (float)Math.Sqrt(sum / count));
    }

    private static void Transform(AudioBlock block, Func<float, float> transform)
    {
        foreach (var channel in block.Channels)
            for (var frame = 0; frame < block.Frames; frame++)
                channel[frame] = transform(channel[frame]);
    }
}
