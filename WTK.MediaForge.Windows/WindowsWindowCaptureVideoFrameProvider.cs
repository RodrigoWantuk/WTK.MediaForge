using System.Diagnostics;
using Vortice.DXGI;
using WTK.MediaForge.Capture.Gpu;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

internal sealed class WindowsWindowCaptureVideoFrameProvider : IVideoFrameProvider, IAsyncDisposable, IDisposable
{
    private const int SlotCount = 3;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(11);

    private readonly WindowCaptureSourceSettings _settings;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IWindowsGraphicsCaptureSessionFactory _sessionFactory;
    private readonly RetiredGpuResourceManager _retiredResources = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private IWindowsGraphicsCaptureSession? _session;
    private D3D11GpuFrameSlotRing? _slotRing;
    private CancellationTokenSource? _captureCancellation;
    private Thread? _captureThread;
    private TaskCompletionSource? _startCompletion;
    private TaskCompletionSource? _captureCompletion;
    private long _frameNumber;
    private int _state = (int)MediaSourceState.Stopped;
    private int _disposeState;

    public WindowsWindowCaptureVideoFrameProvider(
        SourceId id,
        string name,
        WindowCaptureSourceSettings settings,
        IMediaForgeDiagnosticsSink? diagnostics = null,
        IWindowsGraphicsCaptureSessionFactory? sessionFactory = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Source id cannot be empty.", nameof(id));

        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Window" : name;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _diagnostics = diagnostics;
        _sessionFactory = sessionFactory ?? new WindowsGraphicsCaptureSessionFactory();
    }

    public SourceId Id { get; }

    public string Name { get; }

    public MediaSourceState State => (MediaSourceState)Volatile.Read(ref _state);

    public Exception? LastError { get; private set; }

    internal int ActiveSlotRetainCount
    {
        get
        {
            var ring = Volatile.Read(ref _slotRing);
            if (ring is null)
                return 0;

            var total = 0;
            for (var slot = 0; slot < ring.Ring.SlotCount; slot++)
                total += ring.Ring.GetRefCount(slot);
            return total;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (State is MediaSourceState.Running or MediaSourceState.Starting)
                return;

            LastError = null;
            Volatile.Write(ref _state, (int)MediaSourceState.Starting);
            _captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _startCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureThread = new Thread(CaptureThreadMain)
            {
                IsBackground = true,
                Name = $"WindowCapture-{Id}"
            };
            _captureThread.SetApartmentState(ApartmentState.MTA);
            _captureThread.Start();
            await _startCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _captureCancellation?.Cancel();
            throw;
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
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease)
    {
        lease = null!;
        ThrowIfDisposed();
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
                    _retiredResources.TryFinalizeAll();
                }
            },
            onReleaseFailure: ex =>
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "source.window_capture_lease_release_failed",
                    $"Window source '{Name}' failed to release a GPU frame lease.",
                    nameof(WindowsWindowCaptureVideoFrameProvider),
                    ex,
                    Id.Value,
                    Name));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposeState) == 2)
            return;

        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposeState) == 2)
                return;

            Volatile.Write(ref _disposeState, 1);
            if (_retiredResources.FailedCount > 0)
                _retiredResources.RequeueFailedResourcesForRetry();

            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            await _retiredResources
                .WaitForAllFinalizedAsync(StopTimeout, CancellationToken.None)
                .ConfigureAwait(false);

            Volatile.Write(ref _disposeState, 2);
        }
        catch
        {
            Volatile.Write(ref _disposeState, 3);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        _lifecycleGate.Dispose();
    }

    public void Dispose()
    {
        try
        {
            DisposeAsync().AsTask().WaitAsync(DisposeTimeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.window_capture_dispose_failed",
                $"Window source '{Name}' failed to dispose cleanly.",
                nameof(WindowsWindowCaptureVideoFrameProvider),
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
            var ring = CreateRing(session);
            Volatile.Write(ref _slotRing, ring);
            Volatile.Write(ref _state, (int)MediaSourceState.Running);

            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Info,
                "source.window_capture_started",
                $"Window source '{Name}' started for '{session.WindowTitle}' at {session.FrameSize.Width}x{session.FrameSize.Height}.",
                nameof(WindowsWindowCaptureVideoFrameProvider),
                sourceId: Id.Value,
                sourceName: Name);

            _startCompletion!.TrySetResult();
            CaptureLoop(session, _captureCancellation!.Token);
        }
        catch (Exception) when (_captureCancellation?.IsCancellationRequested == true)
        {
            _startCompletion?.TrySetCanceled(_captureCancellation.Token);
        }
        catch (Exception ex)
        {
            LastError = ex;
            Volatile.Write(ref _state, (int)MediaSourceState.Failed);
            _startCompletion?.TrySetException(ex);
            MediaForgeDiagnostics.Report(
                _diagnostics,
                MediaForgeDiagnosticSeverity.Error,
                "source.window_capture_failed",
                $"Window source '{Name}' failed: {ex.Message}",
                nameof(WindowsWindowCaptureVideoFrameProvider),
                ex,
                Id.Value,
                Name,
                Volatile.Read(ref _frameNumber));
        }
        finally
        {
            _captureCompletion?.TrySetResult();
        }
    }

    private void CaptureLoop(IWindowsGraphicsCaptureSession session, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            EnsureRingMatchesSession(session);
            var ring = Volatile.Read(ref _slotRing);
            if (ring is null)
                break;
            if (!ring.Ring.TryBeginWrite(out var slotIndex))
            {
                cancellationToken.WaitHandle.WaitOne(1);
                continue;
            }

            var handle = ring.GetHandle(slotIndex);
            try
            {
                if (!session.TryCaptureNextFrameTo(handle, cancellationToken))
                {
                    ring.Ring.CancelWrite(slotIndex);
                    continue;
                }

                ring.Ring.CompleteWrite(
                    slotIndex,
                    handle,
                    Interlocked.Increment(ref _frameNumber),
                    Stopwatch.GetTimestamp());
                _retiredResources.TryFinalizeAll();
            }
            catch
            {
                ring.Ring.CancelWrite(slotIndex);
                throw;
            }
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (State == MediaSourceState.Stopped && _session is null && _slotRing is null)
            return;

        Volatile.Write(ref _state, (int)MediaSourceState.Stopping);
        _captureCancellation?.Cancel();
        _session?.RequestStop();

        var completion = _captureCompletion?.Task;
        if (completion is not null)
        {
            try
            {
                await completion.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                LastError = ex;
                Volatile.Write(ref _state, (int)MediaSourceState.Failed);
                throw new TimeoutException(
                    $"Window source '{Name}' capture thread did not stop within {StopTimeout}.",
                    ex);
            }
        }

        RetireCurrentRing();
        _retiredResources.TryFinalizeAll();
        _session?.Dispose();
        _session = null;
        _captureThread = null;
        _captureCancellation?.Dispose();
        _captureCancellation = null;
        _captureCompletion = null;
        _startCompletion = null;
        Volatile.Write(ref _state, (int)MediaSourceState.Stopped);
    }

    private void EnsureRingMatchesSession(IWindowsGraphicsCaptureSession session)
    {
        var ring = Volatile.Read(ref _slotRing);
        if (ring is null)
            return;

        var size = session.FrameSize;
        var handle = ring.GetHandle(0);
        if (handle.TextureSize == size)
            return;

        var replacement = CreateRing(session);
        var retired = Interlocked.Exchange(ref _slotRing, replacement);
        if (retired is not null)
        {
            retired.Retire();
            _retiredResources.Add(retired);
        }
    }

    private D3D11GpuFrameSlotRing CreateRing(IWindowsGraphicsCaptureSession session) =>
        new(
            session.Device.Device,
            session.FrameSize.Width,
            session.FrameSize.Height,
            Format.B8G8R8A8_UNorm,
            SlotCount,
            _diagnostics);

    private FrameSize ResolveTextureSize(IGpuFrameHandle? handle) =>
        handle is WTK.MediaForge.Graphics.D3D11.D3D11SharedTextureFrameHandle shared
            ? shared.TextureSize
            : _session?.FrameSize ?? default;

    private void RetireCurrentRing()
    {
        var ring = Interlocked.Exchange(ref _slotRing, null);
        if (ring is null)
            return;

        ring.Retire();
        _retiredResources.Add(ring);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
}
