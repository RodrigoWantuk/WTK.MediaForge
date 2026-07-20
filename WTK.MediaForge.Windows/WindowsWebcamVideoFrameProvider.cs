using System.Diagnostics;
using WTK.MediaForge.Capture.Gpu;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsWebcamVideoFrameProvider : IVideoFrameProvider, IAsyncDisposable, IDisposable
{
    private const int SlotCount = 3;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(11);

    private readonly WebcamSourceSettings _settings;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IWindowsWebcamCaptureSessionFactory _sessionFactory;
    private readonly RetiredGpuResourceManager _retiredResourceManager = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateGate = new();

    private IWindowsWebcamCaptureSession? _session;
    private D3D11GpuFrameSlotRing? _slotRing;
    private CancellationTokenSource? _captureCancellation;
    private Thread? _captureThread;
    private TaskCompletionSource? _startTcs;
    private long _frameNumber;
    private int _disposed;
    private int _state = (int)MediaSourceState.Stopped;

    public WindowsWebcamVideoFrameProvider(
        SourceId id,
        string name,
        WebcamSourceSettings settings,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IWindowsWebcamCaptureSessionFactory? sessionFactory = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Source id cannot be empty.", nameof(id));

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Webcam" : name;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _diagnostics = diagnostics;
        _sessionFactory = sessionFactory ?? new WindowsWebcamCaptureSessionFactory();
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State => (MediaSourceState)Volatile.Read(ref _state);

    public Exception? LastError { get; private set; }

    internal int ActiveSlotRetainCount
    {
        get
        {
            var total = 0;
            var ring = Volatile.Read(ref _slotRing);
            if (ring is not null)
            {
                for (var slot = 0; slot < ring.Ring.SlotCount; slot++)
                    total += ring.Ring.GetRefCount(slot);
            }

            return total;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (State is MediaSourceState.Running or MediaSourceState.Starting)
                return;

            Volatile.Write(ref _state, (int)MediaSourceState.Starting);
            LastError = null;
            _startTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _captureThread = new Thread(CaptureThreadMain)
            {
                IsBackground = true,
                Name = $"WebcamCapture-{Id}"
            };
            _captureThread.Start();
            await _startTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopCore(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        lease = null!;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (State != MediaSourceState.Running)
            return false;

        var slotRing = Volatile.Read(ref _slotRing);
        if (slotRing is null || !slotRing.Ring.TryRetainLatest(out var slotLease))
            return false;

        var frame = slotLease!.Frame with
        {
            SourceId = Id,
            TextureSize = ResolveTextureSize(slotLease.Frame.Handle),
            LogicalSize = ResolveTextureSize(slotLease.Frame.Handle)
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
                    "source.webcam_lease_release_failed",
                    $"Webcam source '{Name}' failed to release a GPU frame lease.",
                    nameof(WindowsWebcamVideoFrameProvider),
                    ex,
                    Id.Value,
                    Name));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            StopCore(CancellationToken.None);
            await _retiredResourceManager
                .WaitForAllFinalizedAsync(StopTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            _captureCancellation?.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            DisposeAsync()
                .AsTask()
                .WaitAsync(DisposeTimeout)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.webcam_dispose_failed",
                $"Webcam source '{Name}' failed to dispose cleanly.",
                nameof(WindowsWebcamVideoFrameProvider),
                ex,
                Id.Value,
                Name);
            throw;
        }
    }

    private void CaptureThreadMain()
    {
        try
        {
            var session = _sessionFactory.Create();
            _session = session;
            session.Start(_settings);

            var ring = new D3D11GpuFrameSlotRing(
                session.Device.Device,
                session.FrameSize.Width,
                session.FrameSize.Height,
                Vortice.DXGI.Format.B8G8R8A8_UNorm,
                SlotCount,
                _diagnostics);
            Volatile.Write(ref _slotRing, ring);

            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Running);

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Info,
                "source.webcam_started",
                $"Webcam source '{Name}' started on '{session.DeviceName}' at {session.FrameSize.Width}x{session.FrameSize.Height}.",
                nameof(WindowsWebcamVideoFrameProvider),
                sourceId: Id.Value,
                sourceName: Name);

            _startTcs!.TrySetResult();
            CaptureLoop(session, ring, _captureCancellation!.Token);
        }
        catch (Exception ex)
        {
            LastError = ex;
            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Failed);

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.webcam_capture_failed",
                $"Webcam source '{Name}' failed: {ex.Message}",
                nameof(WindowsWebcamVideoFrameProvider),
                ex,
                Id.Value,
                Name);

            _startTcs?.TrySetException(ex);
        }
    }

    private void CaptureLoop(
        IWindowsWebcamCaptureSession session,
        D3D11GpuFrameSlotRing slotRing,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!slotRing.Ring.TryBeginWrite(out var slotIndex))
            {
                if (cancellationToken.WaitHandle.WaitOne(1))
                    break;
                continue;
            }

            var handle = slotRing.GetHandle(slotIndex);
            try
            {
                if (!session.TryCaptureNextFrameTo(handle, cancellationToken))
                {
                    slotRing.Ring.CancelWrite(slotIndex);
                    continue;
                }

                slotRing.Ring.CompleteWrite(
                    slotIndex,
                    handle,
                    Interlocked.Increment(ref _frameNumber),
                    Stopwatch.GetTimestamp());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                slotRing.Ring.CancelWrite(slotIndex);
                break;
            }
            catch (Exception ex)
            {
                slotRing.Ring.CancelWrite(slotIndex);
                LastError = ex;
                lock (_stateGate)
                    Volatile.Write(ref _state, (int)MediaSourceState.Failed);

                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.webcam_capture_loop_failed",
                    $"Webcam source '{Name}' failed while capturing a frame.",
                    nameof(WindowsWebcamVideoFrameProvider),
                    ex,
                    Id.Value,
                    Name,
                    Volatile.Read(ref _frameNumber));
                break;
            }
        }
    }

    private void StopCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (State is MediaSourceState.Stopped or MediaSourceState.Stopping)
            return;

        lock (_stateGate)
            Volatile.Write(ref _state, (int)MediaSourceState.Stopping);

        _captureCancellation?.Cancel();
        var stopRequestFailure = _session?.RequestStop();
        if (stopRequestFailure is not null)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Warning,
                "source.webcam_stop_request_failed",
                $"Webcam source '{Name}' reported a failure while requesting capture shutdown; physical thread completion is still required.",
                nameof(WindowsWebcamVideoFrameProvider),
                stopRequestFailure,
                Id.Value,
                Name);
        }

        if (_captureThread is { IsAlive: true } &&
            !_captureThread.Join(StopTimeout))
        {
            var timeout = new TimeoutException($"Webcam source '{Name}' capture thread did not stop within {StopTimeout}.");
            LastError = timeout;
            lock (_stateGate)
                Volatile.Write(ref _state, (int)MediaSourceState.Failed);
            throw timeout;
        }

        RetireCurrentRing();
        _retiredResourceManager.TryFinalizeAll();
        _session?.Dispose();
        _session = null;
        _captureThread = null;
        _captureCancellation?.Dispose();
        _captureCancellation = null;

        lock (_stateGate)
            Volatile.Write(ref _state, (int)MediaSourceState.Stopped);
    }

    private FrameSize ResolveTextureSize(IGpuFrameHandle? handle)
    {
        if (handle is WTK.MediaForge.Graphics.D3D11.D3D11SharedTextureFrameHandle sharedHandle &&
            sharedHandle.TextureSize.Width > 0 &&
            sharedHandle.TextureSize.Height > 0)
        {
            return sharedHandle.TextureSize;
        }

        return _session?.FrameSize ?? default;
    }

    private void RetireCurrentRing()
    {
        var current = Interlocked.Exchange(ref _slotRing, null);
        if (current is null)
            return;

        current.Retire();
        _retiredResourceManager.Add(current);
    }
}
