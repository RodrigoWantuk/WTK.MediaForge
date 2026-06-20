using WTK.MediaForge.Composition.Snapshots;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class ImmediateRenderFrameSubmission : IRenderFrameSubmission
{
    private RenderFrameSnapshot? _snapshot;

    public ImmediateRenderFrameSubmission(RenderFrameSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public bool IsCompleted => true;

    public void Dispose()
    {
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
    }
}
