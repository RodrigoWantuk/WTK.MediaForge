using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

public sealed class SlowNullRenderBackend : IRenderBackend
{
    private readonly TimeSpan _renderDelay;
    private readonly NullRenderBackend _inner = new();

    public SlowNullRenderBackend(TimeSpan renderDelay) =>
        _renderDelay = renderDelay;

    public int RenderCount => _inner.RenderCount;

    public void BindOutput(RenderOutputBindingSnapshot binding) =>
        _inner.BindOutput(binding);

    public void UnbindOutput(RenderOutputId outputId) =>
        _inner.UnbindOutput(outputId);

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) =>
        _inner.ResizeOutput(outputId, surfaceSize);

    public void Render(RenderFrameSnapshot snapshot)
    {
        RenderThreadGuard.AssertOnRenderThread();

        if (_renderDelay > TimeSpan.Zero)
            Thread.Sleep(_renderDelay);

        _inner.Render(snapshot);
    }
}
