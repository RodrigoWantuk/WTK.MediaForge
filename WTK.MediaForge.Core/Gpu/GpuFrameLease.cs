namespace WTK.MediaForge.Core.Gpu;

public sealed class GpuFrameLease : IDisposable
{
    private int _disposed;
    private readonly Action? _onRelease;
    private readonly Action<Exception>? _onReleaseFailure;

    private GpuFrameLease(GpuFrameReference frame, Action? onRelease, Action<Exception>? onReleaseFailure)
    {
        Frame = frame;
        _onRelease = onRelease;
        _onReleaseFailure = onReleaseFailure;
    }

    public GpuFrameReference Frame { get; }

    public static GpuFrameLease Create(
        GpuFrameReference frame,
        Action? onRelease = null,
        Action<Exception>? onReleaseFailure = null) =>
        new(frame, onRelease, onReleaseFailure);

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
        catch (Exception ex)
        {
            _onReleaseFailure?.Invoke(ex);
        }
    }
}
