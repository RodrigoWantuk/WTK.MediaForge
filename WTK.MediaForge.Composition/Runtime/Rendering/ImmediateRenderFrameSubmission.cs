using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class ImmediateRenderFrameSubmission : IRenderFrameSubmission
{
    private readonly RenderedOutputFrameBatch _outputFrames;
    private RenderFrameSnapshot? _snapshot;
    private int _resourcesDisposed;
    private int _outputFramesAcquired;

    public ImmediateRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _outputFrames = RenderedOutputFrameBatch.FromSnapshot(snapshot);
    }

    public bool IsCompleted => true;

    public bool OutputFramesAcquired => Volatile.Read(ref _outputFramesAcquired) != 0;

    public bool HasOutstandingOutputFrameLeases => _outputFrames.HasOutstandingLeases;

    public RenderedOutputFrameBatch AcquireOutputFrames()
    {
        Interlocked.Exchange(ref _outputFramesAcquired, 1);
        return _outputFrames;
    }

    public ValueTask WaitForOutputFrameLeasesAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _outputFrames.WaitForLeasesReleasedAsync(timeout, cancellationToken);

    public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void DisposeCompleted()
    {
        if (!IsCompleted)
            throw new InvalidOperationException("Submission is not completed.");

        if (HasOutstandingOutputFrameLeases)
            throw new InvalidOperationException("Submission still has outstanding output frame leases.");

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }
}
