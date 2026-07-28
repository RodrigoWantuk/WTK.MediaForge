using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal interface IRenderFrameSubmission
{
    /// <summary>
    /// Non-blocking completion probe. Must not wait on GPU work.
    /// </summary>
    bool IsCompleted { get; }

    bool OutputFramesAcquired { get; }

    bool HasOutstandingOutputFrameLeases { get; }

    /// <summary>
    /// True when this submission was produced from a physical RenderGraph whose encoded-output
    /// dispatch operations control delivery to encoder routes.
    /// </summary>
    bool HasPhysicalEncodedOutputDispatchPlan => false;

    /// <summary>
    /// Encoded surface outputs explicitly dispatched by the physical plan. Consumers must only
    /// use this set when <see cref="HasPhysicalEncodedOutputDispatchPlan"/> is true.
    /// </summary>
    IReadOnlySet<RenderOutputId> EncodedOutputDispatchIds => RenderOutputDispatchSet.Empty;

    RenderedOutputFrameBatch AcquireOutputFrames();

    ValueTask WaitForOutputFrameLeasesAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Destroys submission resources without waiting for GPU completion.
    /// Idempotent when already disposed. Throws if the submission is not completed.
    /// </summary>
    void DisposeCompleted();

    /// <summary>
    /// Waits for GPU completion up to the provided timeout.
    /// </summary>
    ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

internal static class RenderOutputDispatchSet
{
    public static IReadOnlySet<RenderOutputId> Empty { get; } = new HashSet<RenderOutputId>();
}
