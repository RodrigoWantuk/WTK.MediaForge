using System.Buffers;

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
        _pool = pool;
        Block = block;
    }

    public AudioBlock Block { get; }

    public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Return(Block);
}

public sealed class AudioBufferPool
{
    private readonly ArrayPool<float> _samples;
    private readonly int _maximumRetainedBlocks;
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

    public AudioBlockLease Rent(AudioFormat format, AudioQuantum quantum, AudioTimestamp timestamp, long sequence, AudioBlockFlags flags = AudioBlockFlags.None)
    {
        format.Validate();
        quantum.Validate();
        var rented = Interlocked.Increment(ref _rentedBlocks);
        if (rented > _maximumRetainedBlocks)
        {
            Interlocked.Decrement(ref _rentedBlocks);
            throw new InvalidOperationException("Audio buffer pool is bounded and exhausted.");
        }
        UpdateHighWaterMark(rented);
        var channels = new float[format.ChannelCount][];
        for (var index = 0; index < channels.Length; index++)
            channels[index] = _samples.Rent(quantum.Frames);
        return new AudioBlockLease(this, new AudioBlock(format, quantum.Frames, channels)
        {
            Timestamp = timestamp,
            Sequence = sequence,
            Flags = flags
        });
    }

    internal void Return(AudioBlock block)
    {
        foreach (var channel in block.Channels)
        {
            Array.Clear(channel, 0, block.Frames);
            _samples.Return(channel);
        }
        Interlocked.Decrement(ref _rentedBlocks);
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
    bool IsRunning);

public sealed class AudioRuntime
{
    private AudioPhysicalGraphPlan? _publishedPlan;
    private readonly AudioBufferPool _bufferPool;
    private int _running;
    private int _retiredPlanCount;

    public AudioRuntime(AudioBufferPool? bufferPool = null) => _bufferPool = bufferPool ?? new AudioBufferPool();

    public void Publish(AudioPhysicalGraphPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var previous = Interlocked.Exchange(ref _publishedPlan, plan);
        if (previous is not null)
            Interlocked.Increment(ref _retiredPlanCount);
    }

    public void Start() => Interlocked.Exchange(ref _running, 1);
    public void Stop() => Interlocked.Exchange(ref _running, 0);

    public AudioBlockLease ProcessSilence(AudioTimestamp timestamp, long sequence)
    {
        var plan = Volatile.Read(ref _publishedPlan) ?? throw new InvalidOperationException("An audio graph plan must be published before processing.");
        if (Volatile.Read(ref _running) == 0)
            throw new InvalidOperationException("Audio runtime is not running.");
        return _bufferPool.Rent(plan.Graph.Format, plan.Graph.Quantum, timestamp, sequence, AudioBlockFlags.Silence);
    }

    public AudioRuntimeHealth GetHealth()
    {
        var plan = Volatile.Read(ref _publishedPlan);
        return new AudioRuntimeHealth(
            plan?.Fingerprint ?? string.Empty,
            _bufferPool.RentedBlocks,
            _bufferPool.HighWaterMark,
            Volatile.Read(ref _retiredPlanCount),
            Volatile.Read(ref _running) != 0);
    }
}

public static class AudioBusMixer
{
    public static void Mix(IReadOnlyList<AudioBlock> inputs, AudioBlock destination)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(destination);
        foreach (var input in inputs)
        {
            if (input.Format != destination.Format || input.Frames != destination.Frames)
                throw new InvalidOperationException("Audio mixer requires matching planar formats and frame counts.");
            for (var channel = 0; channel < destination.Channels.Length; channel++)
                for (var frame = 0; frame < destination.Frames; frame++)
                    destination.Channels[channel][frame] += input.Channels[channel][frame];
        }
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
