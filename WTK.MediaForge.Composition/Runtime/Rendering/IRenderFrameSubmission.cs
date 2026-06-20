namespace WTK.MediaForge.Composition.Runtime.Rendering;

public interface IRenderFrameSubmission
{
    /// <summary>
    /// Non-blocking completion probe. Must not wait on GPU work.
    /// </summary>
    bool IsCompleted { get; }

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
