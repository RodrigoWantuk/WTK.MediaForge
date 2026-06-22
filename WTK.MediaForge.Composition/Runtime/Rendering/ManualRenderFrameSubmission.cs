using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class ManualRenderFrameSubmission : IRenderFrameSubmission
{
    private readonly RenderedOutputFrameBatch _outputFrames;
    private RenderFrameSnapshot? _snapshot;
    private volatile bool _completed;
    private int _resourcesDisposed;
    private int _outputFramesAcquired;

    public ManualRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _outputFrames = RenderedOutputFrameBatch.FromSnapshot(snapshot);
    }

    public bool IsCompleted => _completed;

    public bool OutputFramesAcquired => Volatile.Read(ref _outputFramesAcquired) != 0;

    public bool HasOutstandingOutputFrameLeases => _outputFrames.HasOutstandingLeases;

    public void Complete() => _completed = true;

    public RenderedOutputFrameBatch AcquireOutputFrames()
    {
        Interlocked.Exchange(ref _outputFramesAcquired, 1);
        return _outputFrames;
    }

    public ValueTask WaitForOutputFrameLeasesAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _outputFrames.WaitForLeasesReleasedAsync(timeout, cancellationToken);

    public async ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_completed)
            return;

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (!_completed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Timed out waiting for manual submission to complete.");

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    public void DisposeCompleted()
    {
        if (!_completed)
            throw new InvalidOperationException("Submission is not completed.");

        if (HasOutstandingOutputFrameLeases)
            throw new InvalidOperationException("Submission still has outstanding output frame leases.");

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

        _outputFrames.DisposeSurfaces();
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }
}
