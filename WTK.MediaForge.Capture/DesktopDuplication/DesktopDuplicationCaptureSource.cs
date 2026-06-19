using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.D3D11;
using ResultCode = Vortice.DXGI.ResultCode;
using SharedResourceFlags = Vortice.DXGI.SharedResourceFlags;

namespace WTK.MediaForge.Capture.DesktopDuplication;

public sealed class DesktopDuplicationCaptureSource : IDisposable
{
    private readonly CaptureSourceInfo _source;

    private D3D11GpuDevice? _device;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _ownedTexture;
    private IDXGIKeyedMutex? _keyedMutex;

    private FrameSize _size;
    private long _frameNumber;
    private bool _disposed;
    private nint _sharedHandle;

    private uint _textureWidth;
    private uint _textureHeight;
    private Format _textureFormat;

    public DesktopDuplicationCaptureSource(CaptureSourceInfo source)
    {
        _source = source;
    }

    public bool IsStarted => _duplication is not null;

    public void Start()
    {
        ThrowIfDisposed();

        if (IsStarted)
            return;

        using IDXGIFactory1 factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        if (factory.EnumAdapters1(_source.AdapterIndex, out IDXGIAdapter1? adapter).Failure || adapter is null)
            throw new InvalidOperationException($"Adapter not found: {_source.AdapterIndex}");

        _device = D3D11GpuDevice.CreateForAdapter(adapter);

        if (adapter.EnumOutputs(_source.OutputIndex, out IDXGIOutput? output).Failure || output is null)
            throw new InvalidOperationException($"Output not found: {_source.OutputIndex}");

        using (output)
        using (IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>())
        {
            _duplication = output1.DuplicateOutput(_device.Device);
        }

        var duplicationDescription = _duplication.Description;

        _textureWidth = duplicationDescription.ModeDescription.Width;
        _textureHeight = duplicationDescription.ModeDescription.Height;
        _textureFormat = duplicationDescription.ModeDescription.Format;

        _size = new FrameSize(_textureWidth, _textureHeight);

        CreateOwnedTexture();
    }

    private void DisposeOwnedTexture()
    {
        _keyedMutex?.Dispose();
        _keyedMutex = null;

        if (_sharedHandle != 0)
        {
            CloseHandle(_sharedHandle);
            _sharedHandle = 0;
        }

        _ownedTexture?.Dispose();
        _ownedTexture = null;
    }

    public void Stop()
    {
        _keyedMutex?.Dispose();
        _keyedMutex = null;

        if (_sharedHandle != 0)
        {
            CloseHandle(_sharedHandle);
            _sharedHandle = 0;
        }

        _ownedTexture?.Dispose();
        _ownedTexture = null;

        _duplication?.Dispose();
        _duplication = null;

        _device?.Dispose();
        _device = null;

        _frameNumber = 0;

        _textureWidth = 0;
        _textureHeight = 0;
        _textureFormat = default;
    }

    public bool TryAcquireNextFrame(out D3D11TextureFrame? frame)
    {
        ThrowIfDisposed();

        frame = null;

        if (_device is null || _duplication is null || _ownedTexture is null)
            return false;

        var result = _duplication.AcquireNextFrame(
            0,
            out OutduplFrameInfo frameInfo,
            out IDXGIResource? desktopResource);

        if (result.Code == ResultCode.WaitTimeout.Code)
            return false;

        if (result.Failure)
            throw new InvalidOperationException($"AcquireNextFrame failed: {result}");

        try
        {
            using ID3D11Texture2D acquiredTexture =
                desktopResource!.QueryInterface<ID3D11Texture2D>();

            if (_keyedMutex is not null)
            {
                _keyedMutex.AcquireSync(0, 1000);
            }

            try
            {
                _device.Context.CopyResource(_ownedTexture, acquiredTexture);
                _device.Context.Flush();
            }
            finally
            {
                if (_keyedMutex is not null)
                {
                    _keyedMutex.ReleaseSync(1);
                }
            }

            _frameNumber++;

            frame = new D3D11TextureFrame(
                _ownedTexture,
                _sharedHandle,
                _size,
                _frameNumber,
                Stopwatch.GetTimestamp());

            return true;
        }
        finally
        {
            desktopResource?.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    private void CreateOwnedTexture()
    {
        if (_device is null)
            throw new InvalidOperationException("D3D11 device was not created.");

        if (_textureWidth <= 0 || _textureHeight <= 0)
            throw new InvalidOperationException("Invalid duplication texture size.");

        var description = new Texture2DDescription
        {
            Width = _textureWidth,
            Height = _textureHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = _textureFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags =
                ResourceOptionFlags.SharedNTHandle |
                ResourceOptionFlags.SharedKeyedMutex
        };

        _ownedTexture = _device.Device.CreateTexture2D(description);

        _sharedHandle = CreateSharedHandle(_ownedTexture);

        if (_sharedHandle == 0)
            throw new InvalidOperationException("Failed to create D3D11 shared handle.");

        _keyedMutex = _ownedTexture.QueryInterface<IDXGIKeyedMutex>();
    }

    private static nint CreateSharedHandle(ID3D11Texture2D texture)
    {
        using IDXGIResource1 resource = texture.QueryInterface<IDXGIResource1>();

        return resource.CreateSharedHandle(
            null,
            SharedResourceFlags.Read | SharedResourceFlags.Write,
            null);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DesktopDuplicationCaptureSource));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}