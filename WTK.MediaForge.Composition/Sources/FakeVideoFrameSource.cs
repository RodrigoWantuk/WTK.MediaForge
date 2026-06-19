using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;

namespace WTK.MediaForge.Composition.Sources;

public sealed class FakeVideoFrameSource : IVideoFrameProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly FrameSize _frameSize;

    private GpuFrameReference _latestFrame;
    private bool _hasFrame;
    private int _retainCount;
    private int _disposed;

    public FakeVideoFrameSource(SourceId id, string name, FrameSize frameSize)
    {
        Id = id;
        Name = name;
        _frameSize = frameSize;
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public Exception? LastError { get; private set; }

    internal int RetainCount
    {
        get
        {
            lock (_gate)
                return _retainCount;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        State = MediaSourceState.Running;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            State = MediaSourceState.Stopped;
            _hasFrame = false;
        }

        return Task.CompletedTask;
    }

    public void PublishFrame(long frameNumber, MediaTime timestamp)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var frame = new GpuFrameReference
        {
            SourceId = Id,
            Backend = GpuFrameBackend.CpuBitmap,
            Handle = new FakeGpuFrameHandle { Token = frameNumber },
            TextureSize = _frameSize,
            LogicalSize = _frameSize,
            FrameNumber = frameNumber,
            Timestamp = timestamp
        };

        lock (_gate)
        {
            _latestFrame = frame;
            _hasFrame = true;
        }
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_gate)
        {
            if (State != MediaSourceState.Running || !_hasFrame)
            {
                lease = null!;
                return false;
            }

            _retainCount++;
            lease = GpuFrameLease.Create(_latestFrame, ReleaseRetain);
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_gate)
        {
            State = MediaSourceState.Stopped;
            _hasFrame = false;
        }
    }

    private void ReleaseRetain()
    {
        lock (_gate)
            _retainCount--;
    }
}
