using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;

namespace WTK.MediaForge.Composition.Sources;

internal sealed class FakeGpuFrameSlotRingVideoFrameSource : IVideoFrameProvider, IDisposable
{
    private readonly GpuFrameSlotRing _ring;
    private readonly FrameSize _frameSize;
    private int _disposed;

    public FakeGpuFrameSlotRingVideoFrameSource(
        SourceId id,
        string name,
        FrameSize frameSize,
        int slotCount = 3)
    {
        Id = id;
        Name = name;
        _frameSize = frameSize;
        _ring = new GpuFrameSlotRing(slotCount);
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public Exception? LastError { get; private set; }

    internal GpuFrameSlotRing Ring => _ring;

    internal int ActiveSlotRetainCount
    {
        get
        {
            var total = 0;

            for (var i = 0; i < _ring.SlotCount; i++)
                total += _ring.GetRefCount(i);

            return total;
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
        _ring.Stop();
        State = MediaSourceState.Stopped;
        return Task.CompletedTask;
    }

    public bool TryCaptureFrame(long frameNumber, MediaTime timestamp)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (State != MediaSourceState.Running)
            return false;

        if (!_ring.TryBeginWrite(out var slotIndex))
            return false;

        _ring.CompleteWrite(
            slotIndex,
            new FakeGpuFrameSlotHandle
            {
                SlotIndex = slotIndex,
                ContentToken = frameNumber
            },
            frameNumber);

        return true;
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (State != MediaSourceState.Running || !_ring.TryRetainLatest(out var slotLease))
        {
            lease = null!;
            return false;
        }

        var frame = slotLease!.Frame with
        {
            SourceId = Id,
            TextureSize = _frameSize,
            LogicalSize = _frameSize,
            Timestamp = MediaTime.Zero
        };

        lease = GpuFrameLease.Create(frame, slotLease.Dispose);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        State = MediaSourceState.Stopped;
        _ring.Dispose();
    }
}
