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
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.DesktopDuplication;

internal enum ProviderDisposeState
{
    Active,
    Disposing,
    DisposeTimedOut,
    Disposed,
}

public sealed class DesktopDuplicationFrameProvider : IVideoFrameProvider, IAsyncDisposable, IDisposable
{
    private const int SlotCount = 3;
    private const int DisposeWaitSeconds = 5;

    private readonly CaptureSourceInfo _captureSource;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly object _stateGate = new();
    private readonly RetiredGpuResourceManager _retiredResourceManager = new();
    private readonly SemaphoreSlim _disposeGate = new(1, 1);

    private DesktopDuplicationSession? _session;
    private D3D11GpuFrameSlotRing? _slotRing;
    private CancellationTokenSource? _captureCts;
    private Thread? _captureThread;
    private TaskCompletionSource? _startTcs;
    private long _frameNumber;
    private int _disposed;
    private int _disposeState = (int)ProviderDisposeState.Active;
    private int _state = (int)MediaSourceState.Stopped;

    public DesktopDuplicationFrameProvider(
        SourceId id,
        CaptureSourceInfo captureSource,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        Id = id;
        _captureSource = captureSource ?? throw new ArgumentNullException(nameof(captureSource));
        _diagnostics = diagnostics;
    }

    public SourceId Id { get; }

    public string Name => _captureSource.OutputName;

    public MediaSourceState State => (MediaSourceState)Volatile.Read(ref _state);

    public Exception? LastError { get; private set; }

    internal GpuFrameSlotRing? Ring => Volatile.Read(ref _slotRing)?.Ring;

    internal RetiredGpuResourceManager RetiredResourceManager => _retiredResourceManager;

    internal ProviderDisposeState DisposeState => (ProviderDisposeState)Volatile.Read(ref _disposeState);

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

            foreach (var retired in _retiredResourceManager.PendingResources)
            {
                if (retired is D3D11GpuFrameSlotRing ring)
                {
                    for (var i = 0; i < ring.Ring.SlotCount; i++)
                        total += ring.Ring.GetRefCount(i);
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

        if (_captureThread is { IsAlive: true } &&
            !_captureThread.Join(TimeSpan.FromSeconds(5)))
        {
            var timeout = new TimeoutException("Capture thread did not stop within the expected timeout.");

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "capture.thread_stop_timeout",
                timeout.Message,
                nameof(DesktopDuplicationFrameProvider),
                timeout,
                Id.Value,
                Name);

            lock (_stateGate)
            {
                Volatile.Write(ref _state, (int)MediaSourceState.Failed);
                LastError = timeout;
            }

            throw timeout;
        }

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

        var ownerRing = slotRing;
        lease = GpuFrameLease.Create(
            frame,
            onRelease: () =>
            {
                try
                {
                    slotLease.Dispose();
                }
                finally
                {
                    if (ownerRing.IsRetired)
                        ownerRing.TryFinalizePhysicalResources();

                    _retiredResourceManager.TryFinalizeAll();
                }
            },
            onReleaseFailure: ex =>
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "capture.lease_release_failed",
                    "Failed to release GPU frame lease.",
                    nameof(DesktopDuplicationFrameProvider),
                    ex,
                    Id.Value,
                    Name));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if ((ProviderDisposeState)Volatile.Read(ref _disposeState) == ProviderDisposeState.Disposed)
                return;

            Volatile.Write(ref _disposeState, (int)ProviderDisposeState.Disposing);

            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
                await _retiredResourceManager
                    .WaitForAllFinalizedAsync(TimeSpan.FromSeconds(DisposeWaitSeconds), CancellationToken.None)
                    .ConfigureAwait(false);

                Volatile.Write(ref _disposeState, (int)ProviderDisposeState.Disposed);
                Interlocked.Exchange(ref _disposed, 1);
            }
            catch (TimeoutException ex)
            {
                LastError = ex;

                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "capture.dispose_timeout",
                    ex.Message,
                    nameof(DesktopDuplicationFrameProvider),
                    ex,
                    Id.Value,
                    Name);

                lock (_stateGate)
                    Volatile.Write(ref _state, (int)MediaSourceState.Failed);

                Volatile.Write(ref _disposeState, (int)ProviderDisposeState.DisposeTimedOut);
                throw;
            }
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

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
                    SlotCount,
                    _diagnostics));

            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Running);

            _startTcs!.TrySetResult();

            CaptureLoop(_captureCts!.Token);
        }
        catch (Exception ex)
        {
            LastError = ex;

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Fatal,
                "capture.thread_failed",
                ex.Message,
                nameof(DesktopDuplicationFrameProvider),
                ex,
                Id.Value,
                Name);

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
            SlotCount,
            _diagnostics);

        var oldRing = Interlocked.Exchange(ref _slotRing, newRing);

        if (oldRing is not null)
        {
            oldRing.Retire();
            _retiredResourceManager.Add(oldRing);
        }
    }

    private void RetireCurrentRing()
    {
        var current = Interlocked.Exchange(ref _slotRing, null);

        if (current is null)
            return;

        current.Retire();
        _retiredResourceManager.Add(current);
    }

    private void TryFinalizeRetiredRings() => _retiredResourceManager.TryFinalizeAll();
}
