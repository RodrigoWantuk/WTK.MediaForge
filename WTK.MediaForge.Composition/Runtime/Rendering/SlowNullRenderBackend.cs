using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class SlowNullRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly TimeSpan _renderDelay;

    public SlowNullRenderBackend(RenderThreadGuard threadGuard, TimeSpan renderDelay)
    {
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _renderDelay = renderDelay;
    }

    public int RenderCount => Volatile.Read(ref _renderCount);

    private int _renderCount;

    public void BindOutput(RenderOutputBindingSnapshot binding) { }

    public void UnbindOutput(RenderOutputId outputId) { }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

    public void Render(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_renderDelay > TimeSpan.Zero)
            Thread.Sleep(_renderDelay);

        Interlocked.Increment(ref _renderCount);
    }
}
