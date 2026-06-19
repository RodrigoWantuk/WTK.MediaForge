using System.Diagnostics;

namespace WTK.MediaForge.Core.Time;

public readonly record struct MediaTime(long TimestampNs)
{
    public static MediaTime Zero => new(0);

    public static MediaTime FromStopwatchTicks(long ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks));

        double seconds = (double)ticks / Stopwatch.Frequency;
        long nanoseconds = (long)(seconds * 1_000_000_000L);
        return new MediaTime(nanoseconds);
    }

    public override string ToString() => $"{TimestampNs} ns";
}
