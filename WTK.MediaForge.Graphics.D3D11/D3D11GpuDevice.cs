using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WTK.MediaForge.Graphics.D3D11;

public sealed class D3D11GpuDevice : IDisposable
{
    private bool _disposed;

    private D3D11GpuDevice(
        IDXGIAdapter1 adapter,
        ID3D11Device device,
        ID3D11DeviceContext context)
    {
        Adapter = adapter;
        Device = device;
        Context = context;
    }

    public IDXGIAdapter1 Adapter { get; }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    public static D3D11GpuDevice CreateForAdapter(
        IDXGIAdapter1 adapter,
        bool requireVideoSupport = false)
    {
        var creationFlags =
            DeviceCreationFlags.BgraSupport |
#if DEBUG
            DeviceCreationFlags.Debug;
#else
            DeviceCreationFlags.None;
#endif
        if (requireVideoSupport)
            creationFlags |= DeviceCreationFlags.VideoSupport;

        var featureLevels = new[]
        {
            FeatureLevel.Level_12_1,
            FeatureLevel.Level_12_0,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0
        };

        Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            adapter,
            DriverType.Unknown,
            creationFlags,
            featureLevels,
            out ID3D11Device device,
            out ID3D11DeviceContext context);
        if (requireVideoSupport)
        {
            using var multithread = device.QueryInterfaceOrNull<ID3D11Multithread>();
            multithread?.SetMultithreadProtected(true);
        }

        return new D3D11GpuDevice(adapter, device, context);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Context.Dispose();
        Device.Dispose();
        Adapter.Dispose();
    }
}
