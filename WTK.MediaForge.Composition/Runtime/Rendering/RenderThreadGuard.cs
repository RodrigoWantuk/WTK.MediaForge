namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Per render-thread instance guard. Each <see cref="MediaForgeRenderThread"/> owns one guard
/// shared with its backend — not a process-wide singleton.
/// </summary>
internal sealed class RenderThreadGuard
{
    private int _renderThreadId;

    public void BindToCurrentThread() =>
        _renderThreadId = Environment.CurrentManagedThreadId;

    public void Clear() =>
        _renderThreadId = 0;

    public void AssertOnRenderThread()
    {
        if (_renderThreadId == 0)
            throw new InvalidOperationException("Render thread has not been bound to this guard.");

        if (Environment.CurrentManagedThreadId != _renderThreadId)
            throw new InvalidOperationException("Render backend calls must run on the render thread.");
    }
}
