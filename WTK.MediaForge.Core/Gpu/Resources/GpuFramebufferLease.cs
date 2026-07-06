namespace WTK.MediaForge.Core.Gpu.Resources;

internal sealed class GpuFramebufferLease : IDisposable
{
    private GpuFramebuffer? _framebuffer;
    private readonly Action<GpuFramebuffer> _onRelease;
    private int _disposed;

    internal GpuFramebufferLease(GpuFramebuffer framebuffer, Action<GpuFramebuffer> onRelease)
    {
        _framebuffer = framebuffer ?? throw new ArgumentNullException(nameof(framebuffer));
        _onRelease = onRelease ?? throw new ArgumentNullException(nameof(onRelease));
        framebuffer.AddLeaseRef();
    }

    internal GpuFramebuffer Framebuffer =>
        _framebuffer ?? throw new ObjectDisposedException(nameof(GpuFramebufferLease));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var framebuffer = Interlocked.Exchange(ref _framebuffer, null);
        if (framebuffer is null)
            return;

        _onRelease(framebuffer);
    }
}
