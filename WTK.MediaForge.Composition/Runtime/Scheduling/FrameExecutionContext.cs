using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Scheduling;

internal sealed class FrameSynchronizationPrimitives
{
    public static FrameSynchronizationPrimitives Empty { get; } = new();

    public IReadOnlyList<long> PendingFenceIds { get; init; } = Array.Empty<long>();
}

internal sealed class FrameExecutionContext
{
    public required long FrameId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public required TimeSpan FrameBudget { get; init; }

    public IReadOnlyList<RenderOutputId> TargetOutputs { get; init; } = Array.Empty<RenderOutputId>();

    public FrameSynchronizationPrimitives Synchronization { get; init; } =
        FrameSynchronizationPrimitives.Empty;
}

internal interface IFrameSchedulerTarget
{
    void OnScheduledFrame(FrameExecutionContext context);
}
