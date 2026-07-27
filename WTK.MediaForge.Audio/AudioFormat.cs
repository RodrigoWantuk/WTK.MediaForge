namespace WTK.MediaForge.Audio;

public enum AudioChannelLayout
{
    Mono = 1,
    Stereo = 2
}

public enum AudioSampleFormat
{
    Float32Planar = 0
}

public readonly record struct AudioFormat(int SampleRate, AudioChannelLayout ChannelLayout, AudioSampleFormat SampleFormat = AudioSampleFormat.Float32Planar)
{
    public const int ProductSampleRate = 48_000;

    public int ChannelCount => (int)ChannelLayout;

    public static AudioFormat Mono => new(ProductSampleRate, AudioChannelLayout.Mono);
    public static AudioFormat Stereo => new(ProductSampleRate, AudioChannelLayout.Stereo);

    public void Validate()
    {
        if (SampleRate != ProductSampleRate)
            throw new ArgumentOutOfRangeException(nameof(SampleRate), "Audio currently requires 48 kHz.");
        if (ChannelLayout is not AudioChannelLayout.Mono and not AudioChannelLayout.Stereo)
            throw new ArgumentOutOfRangeException(nameof(ChannelLayout), "Audio currently supports mono or stereo.");
        if (SampleFormat != AudioSampleFormat.Float32Planar)
            throw new ArgumentOutOfRangeException(nameof(SampleFormat), "Audio currently requires float32 planar samples.");
    }
}

public readonly record struct AudioQuantum(int Frames)
{
    public const int TwoPointFiveMillisecondsFrames = 120;
    public const int FiveMillisecondsFrames = 240;
    public const int DefaultFrames = 480;
    public const int TwentyMillisecondsFrames = 960;

    public static AudioQuantum Default => new(DefaultFrames);

    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / (double)AudioFormat.ProductSampleRate);

    public void Validate(bool allowModelOnlyQuantum = true)
    {
        var allowed = Frames is FiveMillisecondsFrames or DefaultFrames or TwentyMillisecondsFrames ||
            (allowModelOnlyQuantum && Frames == TwoPointFiveMillisecondsFrames);
        if (!allowed)
            throw new ArgumentOutOfRangeException(nameof(Frames), "Audio quantum must be 2.5, 5, 10, or 20 ms at 48 kHz.");
    }
}

public readonly record struct AudioTimestamp(long MonotonicTicks)
{
    public static AudioTimestamp FromTimeSpan(TimeSpan value) => new(value.Ticks);
    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(MonotonicTicks);
}

[Flags]
public enum AudioBlockFlags
{
    None = 0,
    Silence = 1,
    Discontinuity = 2
}
