using System.Diagnostics;

namespace WTK.MediaForge.Audio;

public sealed class StopwatchAudioClock : IAudioClock
{
    public AudioTimestamp GetTimestamp() => AudioTimestamp.FromTimeSpan(
        TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency));
}

public sealed class AudioDriftEstimator
{
    private long _sampleCount;
    private double _mean;
    private double _m2;

    public long SampleCount => Volatile.Read(ref _sampleCount);
    public double DriftTicks { get; private set; }
    public double JitterTicks => SampleCount < 2 ? 0d : Math.Sqrt(_m2 / (SampleCount - 1));

    public void Observe(AudioTimestamp audio, AudioTimestamp reference)
    {
        var drift = audio.MonotonicTicks - reference.MonotonicTicks;
        var count = Interlocked.Increment(ref _sampleCount);
        var delta = drift - _mean;
        _mean += delta / count;
        _m2 += delta * (drift - _mean);
        DriftTicks = _mean;
    }
}

public sealed class AudioClockSynchronizer(IAudioClock clock)
{
    private readonly IAudioClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private AudioTimestamp? _origin;

    public AudioTimestamp GetSynchronizedTimestamp()
    {
        var timestamp = _clock.GetTimestamp();
        _origin ??= timestamp;
        return new AudioTimestamp(timestamp.MonotonicTicks - _origin.Value.MonotonicTicks);
    }
}

public sealed class AudioVideoSyncCoordinator
{
    private readonly AudioDriftEstimator _driftEstimator = new();

    public AudioDriftEstimator DriftEstimator => _driftEstimator;

    public void Observe(AudioTimestamp audio, AudioTimestamp video) => _driftEstimator.Observe(audio, video);

    public int GetSuggestedResampleFrameAdjustment(AudioQuantum quantum)
    {
        var driftFrames = _driftEstimator.DriftTicks / TimeSpan.TicksPerSecond * AudioFormat.ProductSampleRate;
        return (int)Math.Clamp(Math.Round(-driftFrames), -Math.Max(1, quantum.Frames / 100), Math.Max(1, quantum.Frames / 100));
    }
}

internal static class AudioSourceRenderer
{
    public static void RenderGeneratedTone(AudioSourceDefinition source, AudioBlock block)
    {
        var phaseStep = 2d * Math.PI * source.ToneFrequencyHz / block.Format.SampleRate;
        var phase = block.Sequence * block.Frames * phaseStep;
        for (var frame = 0; frame < block.Frames; frame++)
        {
            var sample = (float)Math.Sin(phase + frame * phaseStep);
            for (var channel = 0; channel < block.Channels.Length; channel++)
                block.Channels[channel][frame] = sample;
        }
    }
}
