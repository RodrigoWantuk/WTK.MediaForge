using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class ImmediateRenderFrameSubmission : IRenderFrameSubmission, IDisposable
{
    private const int DefaultDisposeWaitSeconds = 5;

    private RenderFrameSnapshot? _snapshot;
    private int _resourcesDisposed;

    public ImmediateRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsCompleted => true;

    public ValueTask WaitForCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void DisposeCompleted()
    {
        if (!IsCompleted)
            throw new InvalidOperationException("Submission is not completed.");

        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            return;

        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }

    public void Dispose()
    {
        WaitForCompletionAsync(TimeSpan.FromSeconds(DefaultDisposeWaitSeconds), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        DisposeCompleted();
    }

    public ValueTask DisposeAsync()
    {
        DisposeCompleted();
        return ValueTask.CompletedTask;
    }
}
