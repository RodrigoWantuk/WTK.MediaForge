namespace WTK.MediaForge.Core.Media.Encode;

/// <summary>
/// Scheduler-owned encode frame metadata. No CPU pixel buffers.
/// </summary>
public sealed class HardwareEncodeFrameContext
{
    public required long FrameId { get; init; }

    public TimeSpan PresentationTime { get; init; }

    public TimeSpan FrameBudget { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
