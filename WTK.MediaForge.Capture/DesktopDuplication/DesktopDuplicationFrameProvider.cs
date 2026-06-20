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

    private DesktopDuplicationSession? _session;
    private D3D11GpuFrameSlotRing? _slotRing;
    private CancellationTokenSource? _captureCts;
    private Thread? _captureThread;
    private TaskCompletionSource? _startTcs;
    private long _frameNumber;
    private int _disposed;

    public DesktopDuplicationFrameProvider(SourceId id, CaptureSourceInfo captureSource)
    {
        Id = id;
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
    }

    public SourceId Id { get; }

    public string Name => _captureSource.OutputName;

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public Exception? LastError { get; private set; }

    internal GpuFrameSlotRing? Ring => _slotRing?.Ring;

    internal int ActiveSlotRetainCount
    {
        get
        {
            var ring = _slotRing?.Ring;
            if (ring is null)
                return 0;

            var total = 0;

            for (var i = 0; i < ring.SlotCount; i++)
                total += ring.GetRefCount(i);

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

            State = MediaSourceState.Starting;
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

            State = MediaSourceState.Stopping;
        }

        _captureCts?.Cancel();

        if (_captureThread is { IsAlive: true })
            _captureThread.Join(TimeSpan.FromSeconds(5));

        _slotRing?.Ring.Stop();
        _slotRing?.Dispose();
        _slotRing = null;

        _session?.Dispose();
        _session = null;

        _captureCts?.Dispose();
        _captureCts = null;
        _captureThread = null;

        lock (_stateGate)
            State = MediaSourceState.Stopped;

        return Task.CompletedTask;
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        lease = null!;

        if (State != MediaSourceState.Running)
            return false;

        var ring = _slotRing?.Ring;
        if (ring is null || !ring.TryRetainLatest(out var slotLease))
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
    }

    private void CaptureThreadMain()
    {
        try
        {
            _session = new DesktopDuplicationSession();
            _session.Start(_captureSource);

            _slotRing = new D3D11GpuFrameSlotRing(
                _session.Device.Device,
                _session.TextureSize.Width,
                _session.TextureSize.Height,
                _session.TextureFormat,
                SlotCount);

            lock (_stateGate)
                State = MediaSourceState.Running;

            _startTcs!.TrySetResult();

            CaptureLoop(_captureCts!.Token);
        }
        catch (Exception ex)
        {
            LastError = ex;

            lock (_stateGate)
                State = MediaSourceState.Failed;

            _startTcs?.TrySetException(ex);
        }
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_session is null || _slotRing is null)
                break;

            if (!_session.TryAcquireNextFrame(out var desktopTexture, out _))
                continue;

            try
            {
                PublishDesktopFrame(desktopTexture);
            }
            finally
            {
                desktopTexture.Dispose();
                _session.ReleaseFrame();
            }
        }
    }

    private void PublishDesktopFrame(ID3D11Texture2D desktopTexture)
    {
        if (_session is null || _slotRing is null)
            return;

        var description = desktopTexture.Description;

        if (description.Width != _session.TextureSize.Width ||
            description.Height != _session.TextureSize.Height ||
            description.Format != _session.TextureFormat)
        {
            RecreateSlotRing(description);
        }

        if (!_slotRing.Ring.TryBeginWrite(out var slotIndex))
            return;

        var handle = _slotRing.GetHandle(slotIndex);
        var mutexAcquired = false;

        try
        {
            handle.KeyedMutex.AcquireSync(D3D11SharedTextureSyncKeys.Producer, 1000);
            mutexAcquired = true;

            _session.Device.Context.CopyResource(handle.Texture, desktopTexture);
            _session.Device.Context.Flush();
        }
        catch
        {
            _slotRing.Ring.CancelWrite(slotIndex);
            throw;
        }
        finally
        {
            if (mutexAcquired)
                handle.KeyedMutex.ReleaseSync(D3D11SharedTextureSyncKeys.Consumer);
        }

        var frameNumber = Interlocked.Increment(ref _frameNumber);

        _slotRing.Ring.CompleteWrite(
            slotIndex,
            handle,
            frameNumber,
            Stopwatch.GetTimestamp());
    }

    private void RecreateSlotRing(Texture2DDescription description)
    {
        if (_session is null)
            return;

        _slotRing?.Dispose();

        _slotRing = new D3D11GpuFrameSlotRing(
            _session.Device.Device,
            description.Width,
            description.Height,
            description.Format,
            SlotCount);
    }
}
