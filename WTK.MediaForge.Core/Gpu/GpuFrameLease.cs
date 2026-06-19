namespace WTK.MediaForge.Core.Gpu;

public sealed class GpuFrameLease : IDisposable
{
    private int _disposed;
    private readonly Action? _onRelease;

    private GpuFrameLease(GpuFrameReference frame, Action? onRelease)
    {
        Frame = frame;
        _onRelease = onRelease;
    }

    public GpuFrameReference Frame { get; }

    public static GpuFrameLease Create(GpuFrameReference frame, Action? onRelease = null) =>
        new(frame, onRelease);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_onRelease is null)
            return;

        try
        {
            _onRelease.Invoke();
        }
        catch (Exception)
        {
            // TODO: Diagnostics.Record lease release failure.
        }
    }
}
