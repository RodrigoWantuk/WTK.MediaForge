namespace WTK.MediaForge.Core.Media;

public sealed class GpuVideoFrameLease : IDisposable
{
    private int _disposed;
    private readonly Action? _onRelease;

    private GpuVideoFrameLease(GpuVideoFrameDescriptor descriptor, Action? onRelease)
    {
        Descriptor = descriptor;
        _onRelease = onRelease;
    }

    public GpuVideoFrameDescriptor Descriptor { get; }

    public static GpuVideoFrameLease Create(GpuVideoFrameDescriptor descriptor, Action? onRelease = null) =>
        new(descriptor, onRelease);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _onRelease?.Invoke();
    }
}
