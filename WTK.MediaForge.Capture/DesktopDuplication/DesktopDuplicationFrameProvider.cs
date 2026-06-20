using System.Diagnostics;
using Vortice.Direct3D11;
using WTK.MediaForge.Capture.Gpu;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.DesktopDuplication;

public sealed class DesktopDuplicationFrameProvider : IVideoFrameProvider, IDisposable
{
    private const int SlotCount = 3;

    private readonly CaptureSourceInfo _captureSource;
    private readonly object _stateGate = new();
    private readonly List<D3D11GpuFrameSlotRing> _retiredRings = [];

    private DesktopDuplicationSession? _session;
    private D3D11GpuFrameSlotRing? _slotRing;
    private CancellationTokenSource? _captureCts;
    private Thread? _captureThread;
    private TaskCompletionSource? _startTcs;
    private long _frameNumber;
    private int _disposed;
    private int _state = (int)MediaSourceState.Stopped;

    public DesktopDuplicationFrameProvider(SourceId id, CaptureSourceInfo captureSource)
    {
        Id = id;
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
    }

    public SourceId Id { get; }

    public string Name => _captureSource.OutputName;

    public MediaSourceState State => (MediaSourceState)Volatile.Read(ref _state);

    public Exception? LastError { get; private set; }

    internal GpuFrameSlotRing? Ring => Volatile.Read(ref _slotRing)?.Ring;

    internal int ActiveSlotRetainCount
    {
        get
        {
            var total = 0;
            var current = Volatile.Read(ref _slotRing);

            if (current is not null)
            {
                for (var i = 0; i < current.Ring.SlotCount; i++)
                    total += current.Ring.GetRefCount(i);
            }

            lock (_retiredRings)
            {
                foreach (var retired in _retiredRings)
                {
                    for (var i = 0; i < retired.Ring.SlotCount; i++)
                        total += retired.Ring.GetRefCount(i);
                }
            }

            return total;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lock (_stateGate)
        {
            if (State is MediaSourceState.Starting or MediaSourceState.Running)
                return Task.CompletedTask;

            Volatile.Write(ref _state, (int)MediaSourceState.Starting);
            LastError = null;
            _startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        _captureThread = new Thread(CaptureThreadMain)
        {
            IsBackground = true,
            Name = $"DesktopDuplication-{Id}"
        };
        _captureThread.Start();

        return _startTcs.Task.WaitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (State is MediaSourceState.Stopped or MediaSourceState.Stopping)
                return Task.CompletedTask;

            Volatile.Write(ref _state, (int)MediaSourceState.Stopping);
        }

        _captureCts?.Cancel();

        if (_captureThread is { IsAlive: true })
            _captureThread.Join(TimeSpan.FromSeconds(5));

        RetireCurrentRing();
        TryFinalizeRetiredRings();

        _session?.Dispose();
        _session = null;

        _captureCts?.Dispose();
        _captureCts = null;
        _captureThread = null;

        lock (_stateGate)
            Volatile.Write(ref _state, (int)MediaSourceState.Stopped);

        return Task.CompletedTask;
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lease = null!;

        if (State != MediaSourceState.Running)
            return false;

        var slotRing = Volatile.Read(ref _slotRing);
        if (slotRing is null || !slotRing.Ring.TryRetainLatest(out var slotLease))
            return false;

        var frame = slotLease!.Frame with
        {
            SourceId = Id,
            TextureSize = _session?.TextureSize ?? _captureSource.TextureSize,
            LogicalSize = _captureSource.LogicalSize,
            Rotation = _captureSource.Rotation,
        };

        lease = GpuFrameLease.Create(frame, slotLease.Dispose);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        StopAsync(CancellationToken.None).GetAwaiter().GetResult();

        lock (_retiredRings)
        {
            foreach (var retired in _retiredRings)
                retired.Dispose();

            _retiredRings.Clear();
        }
    }

    private void CaptureThreadMain()
    {
        try
        {
            _session = new DesktopDuplicationSession();
            _session.Start(_captureSource);

            Volatile.Write(
                ref _slotRing,
                new D3D11GpuFrameSlotRing(
                    _session.Device.Device,
                    _session.TextureSize.Width,
                    _session.TextureSize.Height,
                    _session.TextureFormat,
                    SlotCount));

            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Running);

            _startTcs!.TrySetResult();

            CaptureLoop(_captureCts!.Token);
        }
        catch (Exception ex)
        {
            LastError = ex;

            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Failed);

            _startTcs?.TrySetException(ex);
        }
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var session = _session;
            var slotRing = Volatile.Read(ref _slotRing);

            if (session is null || slotRing is null)
                break;

            if (!session.TryAcquireNextFrame(out var desktopTexture, out _))
                continue;

            try
            {
                PublishDesktopFrame(session, slotRing, desktopTexture);
                TryFinalizeRetiredRings();
            }
            finally
            {
                desktopTexture.Dispose();
                session.ReleaseFrame();
            }
        }
    }

    private void PublishDesktopFrame(
        DesktopDuplicationSession session,
        D3D11GpuFrameSlotRing slotRing,
        ID3D11Texture2D desktopTexture)
    {
        var description = desktopTexture.Description;

        if (description.Width != session.TextureSize.Width ||
            description.Height != session.TextureSize.Height ||
            description.Format != session.TextureFormat)
        {
            RecreateSlotRing(session, description);
            slotRing = Volatile.Read(ref _slotRing)!;
        }

        if (!slotRing.Ring.TryBeginWrite(out var slotIndex))
            return;

        var handle = slotRing.GetHandle(slotIndex);
        var mutexAcquired = false;

        try
        {
            handle.KeyedMutex.AcquireSync(handle.ProducerAcquireKey, 1000);
            mutexAcquired = true;

            session.Device.Context.CopyResource(handle.Texture, desktopTexture);
            session.Device.Context.Flush();
        }
        catch
        {
            slotRing.Ring.CancelWrite(slotIndex);
            throw;
        }
        finally
        {
            if (mutexAcquired)
            {
                handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
                handle.NotifyCaptureReleasedToConsumer();
            }
        }

        var frameNumber = Interlocked.Increment(ref _frameNumber);

        slotRing.Ring.CompleteWrite(
            slotIndex,
            handle,
            frameNumber,
            Stopwatch.GetTimestamp());
    }

    private void RecreateSlotRing(DesktopDuplicationSession session, Texture2DDescription description)
    {
        var newRing = new D3D11GpuFrameSlotRing(
            session.Device.Device,
            description.Width,
            description.Height,
            description.Format,
            SlotCount);

        var oldRing = Interlocked.Exchange(ref _slotRing, newRing);

        if (oldRing is not null)
        {
            oldRing.Retire();
            lock (_retiredRings)
                _retiredRings.Add(oldRing);
        }
    }

    private void RetireCurrentRing()
    {
        var current = Interlocked.Exchange(ref _slotRing, null);

        if (current is null)
            return;

        current.Retire();
        lock (_retiredRings)
            _retiredRings.Add(current);
    }

    private void TryFinalizeRetiredRings()
    {
        lock (_retiredRings)
        {
            for (var i = _retiredRings.Count - 1; i >= 0; i--)
            {
                if (_retiredRings[i].TryFinalizePhysicalResources())
                    _retiredRings.RemoveAt(i);
            }
        }
    }
}
