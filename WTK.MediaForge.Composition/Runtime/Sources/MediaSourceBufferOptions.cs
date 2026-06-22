namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class MediaSourceBufferOptions
{
    public MediaSourceBufferMode Mode { get; init; } = MediaSourceBufferMode.KeepLatest;

    public int Capacity { get; init; } = 1;

    public TimeSpan? MaxFrameAge { get; init; }

    internal int NormalizedCapacity => Math.Max(1, Capacity);
}
