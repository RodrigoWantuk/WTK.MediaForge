using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class ManualRenderFrameSubmission : IRenderFrameSubmission
{
    private RenderFrameSnapshot? _snapshot;
    private volatile bool _completed;
    private int _resourcesDisposed;

    public ManualRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsCompleted => _completed;

    public void Complete() => _completed = true;

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

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }
}
