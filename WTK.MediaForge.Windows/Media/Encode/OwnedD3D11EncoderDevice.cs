using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Windows.Media.Encode;

internal sealed class OwnedD3D11EncoderDevice : IDisposable
{
    private readonly D3D11GpuDevice _gpuDevice;
    private bool _disposed;

    private OwnedD3D11EncoderDevice(D3D11GpuDevice gpuDevice) =>
        _gpuDevice = gpuDevice ?? throw new ArgumentNullException(nameof(gpuDevice));

    public ID3D11Device Device
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _gpuDevice.Device;
        }
    }

    public static OwnedD3D11EncoderDevice Create(IDXGIAdapter1 adapter) =>
        new(D3D11GpuDevice.CreateForAdapter(adapter, requireVideoSupport: true));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gpuDevice.Dispose();
    }
}
