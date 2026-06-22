namespace WTK.MediaForge.Composition.Runtime.Sources;

internal sealed class MediaSourceCapabilities
{
    public static MediaSourceCapabilities LiveGpuVideo { get; } = new()
    {
        ProducesVideo = true,
        IsLive = true,
        SupportsGpuFrames = true
    };

    public bool ProducesVideo { get; init; }

    public bool ProducesAudio { get; init; }

    public bool IsLive { get; init; }

    public bool SupportsGpuFrames { get; init; }

    public bool SupportsCpuFrames { get; init; }

    public bool HasStableFrameRate { get; init; }

    public bool CanSeek { get; init; }
}
