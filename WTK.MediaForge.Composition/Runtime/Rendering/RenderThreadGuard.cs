namespace WTK.MediaForge.Composition.Runtime.Rendering;

public static class RenderThreadGuard
{
    private static volatile Thread? _renderThread;

    public static void RegisterRenderThread(Thread thread) =>
        _renderThread = thread ?? throw new ArgumentNullException(nameof(thread));

    public static void ClearRenderThread() =>
        _renderThread = null;

    public static void AssertOnRenderThread()
    {
        var expected = _renderThread;
        if (expected is null)
            throw new InvalidOperationException("Render thread has not been registered.");

        if (Thread.CurrentThread != expected)
            throw new InvalidOperationException("Render backend calls must run on the render thread.");
    }
}
