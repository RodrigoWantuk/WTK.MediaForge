using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class ManualRenderFrameSubmission : IRenderFrameSubmission
{
    private RenderFrameSnapshot? _snapshot;
    private volatile bool _completed;

    public ManualRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsCompleted => _completed;

    public void Complete() => _completed = true;

    public void Dispose()
    {
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }
}
