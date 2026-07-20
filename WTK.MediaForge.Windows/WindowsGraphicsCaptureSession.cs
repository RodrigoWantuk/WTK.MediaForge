using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows;

internal interface IWindowsGraphicsCaptureSession : IDisposable
{
    string WindowTitle { get; }

    FrameSize FrameSize { get; }

    D3D11GpuDevice Device { get; }

    void Start(WindowCaptureSourceSettings settings);

    bool TryCaptureNextFrameTo(D3D11SharedTextureFrameHandle destination, CancellationToken cancellationToken);

    void RequestStop();
}

internal interface IWindowsGraphicsCaptureSessionFactory
{
    IWindowsGraphicsCaptureSession Create();
}

internal sealed class WindowsGraphicsCaptureSessionFactory(
    GpuAdapterAffinityState? adapterAffinity = null) : IWindowsGraphicsCaptureSessionFactory
{
    public IWindowsGraphicsCaptureSession Create() =>
        new WindowsGraphicsCaptureSession(adapterAffinity);
}

internal sealed class WindowsGraphicsCaptureSession : IWindowsGraphicsCaptureSession
{
    private const int KeyedMutexTimeoutMilliseconds = 1000;
    private const int FrameWaitMilliseconds = 100;

    private readonly GpuAdapterAffinityState? _adapterAffinity;
    private readonly AutoResetEvent _frameArrived = new(false);
    private readonly ManualResetEvent _stopRequested = new(false);
    private readonly object _framePoolGate = new();
    private readonly WaitHandle[] _frameWaitHandles;

    private D3D11GpuDevice? _device;
    private IDirect3DDevice? _winRtDevice;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private int _windowClosed;
    private int _started;
    private int _disposeState;

    public WindowsGraphicsCaptureSession(GpuAdapterAffinityState? adapterAffinity = null)
    {
        _adapterAffinity = adapterAffinity;
        _frameWaitHandles = [_frameArrived, _stopRequested];
    }

    public string WindowTitle { get; private set; } = "Window";

    public FrameSize FrameSize { get; private set; }

    public D3D11GpuDevice Device =>
        _device ?? throw new InvalidOperationException("Windows Graphics Capture session has not been started.");

    public void Start(WindowCaptureSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("Windows Graphics Capture session is already started.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || !GraphicsCaptureSession.IsSupported())
            throw new PlatformNotSupportedException("Windows Graphics Capture requires Windows 10 version 2004 or newer.");
        if (settings.WindowHandle == 0)
            throw new ArgumentException("Window capture requires a non-zero window handle.", nameof(settings));

        try
        {
            _stopRequested.Reset();
            Volatile.Write(ref _windowClosed, 0);
            _device = WindowsD3D11AdapterSelector.CreateDevice(_adapterAffinity);
            _winRtDevice = WindowsGraphicsCaptureInterop.CreateWinRtDevice(_device.Device);
            _captureItem = WindowsGraphicsCaptureInterop.CreateItemForWindow((nint)settings.WindowHandle);
            WindowTitle = string.IsNullOrWhiteSpace(_captureItem.DisplayName)
                ? "Window"
                : _captureItem.DisplayName;
            FrameSize = ValidateFrameSize(_captureItem.Size);

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                3,
                ToSizeInt32(FrameSize));
            _framePool.FrameArrived += OnFrameArrived;
            _captureItem.Closed += OnCaptureItemClosed;

            _captureSession = _framePool.CreateCaptureSession(_captureItem);
            _captureSession.IsCursorCaptureEnabled = settings.CaptureCursor;
            _captureSession.StartCapture();
        }
        catch (Exception operationFailure)
        {
            var cleanupFailure = DisposeResources();
            if (cleanupFailure is not null)
            {
                throw new AggregateException(
                    "Windows Graphics Capture failed to start and cleanup also failed.",
                    operationFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    public bool TryCaptureNextFrameTo(
        D3D11SharedTextureFrameHandle destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var waitResult = WaitHandle.WaitAny(_frameWaitHandles, FrameWaitMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        if (waitResult == 1)
        {
            if (Volatile.Read(ref _windowClosed) != 0)
                throw new InvalidOperationException($"Captured window '{WindowTitle}' was closed.");

            return false;
        }

        if (waitResult == WaitHandle.WaitTimeout)
            return false;

        Direct3D11CaptureFrame? frame = null;
        try
        {
            lock (_framePoolGate)
            {
                var framePool = _framePool;
                if (framePool is null)
                    return false;

                while (framePool.TryGetNextFrame() is { } next)
                {
                    frame?.Dispose();
                    frame = next;
                }
            }

            if (frame is null)
                return false;

            var contentSize = ValidateFrameSize(frame.ContentSize);
            if (contentSize != FrameSize)
            {
                FrameSize = contentSize;
                frame.Dispose();
                frame = null;
                RecreateFramePool(contentSize);
                return false;
            }

            using var sourceTexture = WindowsGraphicsCaptureInterop.GetD3D11Texture(frame.Surface);
            var sourceDescription = sourceTexture.Description;
            if (sourceDescription.Width != contentSize.Width ||
                sourceDescription.Height != contentSize.Height ||
                sourceDescription.Format != Format.B8G8R8A8_UNorm)
            {
                throw new InvalidOperationException(
                    $"Windows Graphics Capture produced an unexpected GPU surface: " +
                    $"{sourceDescription.Width}x{sourceDescription.Height} {sourceDescription.Format}.");
            }

            CopyToSharedTexture(sourceTexture, destination);
            return true;
        }
        finally
        {
            frame?.Dispose();
        }
    }

    public void RequestStop() => _stopRequested.Set();

    public void Dispose()
    {
        if (Volatile.Read(ref _disposeState) == 2)
            return;
        if (Interlocked.Exchange(ref _disposeState, 1) == 1)
            throw new InvalidOperationException("Windows Graphics Capture disposal is already in progress.");

        _stopRequested.Set();
        var failure = DisposeResources();
        if (failure is not null)
        {
            Volatile.Write(ref _disposeState, 3);
            throw failure;
        }

        _frameArrived.Dispose();
        _stopRequested.Dispose();
        Volatile.Write(ref _disposeState, 2);
    }

    private void CopyToSharedTexture(
        ID3D11Texture2D source,
        D3D11SharedTextureFrameHandle destination)
    {
        var mutexAcquired = false;
        var copySucceeded = false;
        try
        {
            destination.KeyedMutex.AcquireSync(
                destination.ProducerAcquireKey,
                KeyedMutexTimeoutMilliseconds);
            mutexAcquired = true;
            Device.Context.CopyResource(destination.Texture, source);
            Device.Context.Flush();
            copySucceeded = true;
        }
        finally
        {
            if (mutexAcquired)
            {
                destination.KeyedMutex.ReleaseSync(
                    copySucceeded
                        ? D3D11SharedTextureSyncKeys.Consumer
                        : D3D11SharedTextureSyncKeys.Producer);
                if (copySucceeded)
                    destination.NotifyCaptureReleasedToConsumer();
            }
        }
    }

    private void RecreateFramePool(FrameSize size)
    {
        lock (_framePoolGate)
        {
            if (_framePool is null || _winRtDevice is null || _stopRequested.WaitOne(0))
                return;

            _framePool.Recreate(
                _winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                3,
                ToSizeInt32(size));
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        _ = sender;
        _ = args;
        if (Volatile.Read(ref _disposeState) == 0)
        {
            try
            {
                _frameArrived.Set();
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) == 2)
            {
            }
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        _ = sender;
        _ = args;
        Volatile.Write(ref _windowClosed, 1);
        _stopRequested.Set();
    }

    private Exception? DisposeResources()
    {
        List<Exception>? errors = null;

        TryCleanup(() =>
        {
            if (_captureItem is not null)
                _captureItem.Closed -= OnCaptureItemClosed;
        }, ref errors);
        TryCleanup(() =>
        {
            if (_framePool is not null)
                _framePool.FrameArrived -= OnFrameArrived;
        }, ref errors);

        if (!TryCleanupResource(_captureSession, static value => value.Dispose(), ref errors))
            return new AggregateException("Windows Graphics Capture resource cleanup failed.", errors!);
        _captureSession = null;

        if (!TryCleanupResource(_framePool, static value => value.Dispose(), ref errors))
            return new AggregateException("Windows Graphics Capture resource cleanup failed.", errors!);
        _framePool = null;
        _captureItem = null;

        if (!TryCleanupResource(_winRtDevice as IDisposable, static value => value.Dispose(), ref errors))
            return new AggregateException("Windows Graphics Capture resource cleanup failed.", errors!);
        _winRtDevice = null;

        if (!TryCleanupResource(_device, static value => value.Dispose(), ref errors))
            return new AggregateException("Windows Graphics Capture resource cleanup failed.", errors!);
        _device = null;
        Interlocked.Exchange(ref _started, 0);

        return errors is null
            ? null
            : new AggregateException("Windows Graphics Capture resource cleanup failed.", errors);
    }

    private static void TryCleanup(Action action, ref List<Exception>? errors)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errors ??= [];
            errors.Add(ex);
        }
    }

    private static bool TryCleanupResource<T>(
        T? resource,
        Action<T> dispose,
        ref List<Exception>? errors)
        where T : class
    {
        if (resource is null)
            return true;

        try
        {
            dispose(resource);
            return true;
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
            return false;
        }
    }

    private static FrameSize ValidateFrameSize(SizeInt32 size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException($"Captured window has invalid size {size.Width}x{size.Height}.");

        return new FrameSize(checked((uint)size.Width), checked((uint)size.Height));
    }

    private static SizeInt32 ToSizeInt32(FrameSize size) =>
        new(checked((int)size.Width), checked((int)size.Height));
}
