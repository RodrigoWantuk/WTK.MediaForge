using WTK.MediaForge.Core.Gpu;

namespace WTK.MediaForge.Composition.Runtime.Sources;

internal enum SourceFrameAcquireStatus
{
    Acquired = 0,
    SourceNotRegistered = 1,
    NoFrameAvailable = 2,
    SourceFailed = 3
}

internal readonly struct SourceFrameAcquireResult
{
    private SourceFrameAcquireResult(
        SourceFrameAcquireStatus status,
        GpuFrameLease? lease,
        Exception? error)
    {
        Status = status;
        Lease = lease;
        Error = error;
    }

    public SourceFrameAcquireStatus Status { get; }

    public GpuFrameLease? Lease { get; }

    public Exception? Error { get; }

    public static SourceFrameAcquireResult Acquired(GpuFrameLease lease) =>
        new(SourceFrameAcquireStatus.Acquired, lease, error: null);

    public static SourceFrameAcquireResult SourceNotRegistered() =>
        new(SourceFrameAcquireStatus.SourceNotRegistered, lease: null, error: null);

    public static SourceFrameAcquireResult NoFrameAvailable() =>
        new(SourceFrameAcquireStatus.NoFrameAvailable, lease: null, error: null);

    public static SourceFrameAcquireResult SourceFailed(Exception error) =>
        new(SourceFrameAcquireStatus.SourceFailed, lease: null, error);
}
