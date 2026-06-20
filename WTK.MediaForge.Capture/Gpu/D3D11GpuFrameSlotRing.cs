using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.Gpu;

public sealed class D3D11GpuFrameSlotRing : IDisposable
{
    private readonly D3D11SharedTextureFrameHandle[] _handles;
    private int _handlesDisposed;
    private int _disposed;

    public D3D11GpuFrameSlotRing(
        ID3D11Device device,
        uint width,
        uint height,
        Format format = Format.B8G8R8A8_UNorm,
        int slotCount = 3)
    {
        ArgumentNullException.ThrowIfNull(device);

        Ring = new GpuFrameSlotRing(slotCount, reusePhysicalResources: true);
        _handles = new D3D11SharedTextureFrameHandle[slotCount];

        for (var i = 0; i < slotCount; i++)
        {
            _handles[i] = D3D11SharedTextureFactory.CreateSharedTexture(device, width, height, format);
            Ring.InitializeSlot(i, _handles[i]);
        }
    }

    public GpuFrameSlotRing Ring { get; }

    public bool IsFullyDisposed => Volatile.Read(ref _disposed) != 0;

    public D3D11SharedTextureFrameHandle GetHandle(int slotIndex) => _handles[slotIndex];

    public void Retire() => Ring.Stop();

    public bool TryFinalizePhysicalResources()
    {
        Ring.RequestFinalize();

        if (!Ring.IsFullyDisposed)
            return false;

        DisposeHandlesIfNeeded();
        Volatile.Write(ref _disposed, 1);
        return true;
    }

    public void Dispose()
    {
        if (IsFullyDisposed)
            return;

        TryFinalizePhysicalResources();
    }

    private void DisposeHandlesIfNeeded()
    {
        if (Interlocked.Exchange(ref _handlesDisposed, 1) != 0)
            return;

        foreach (var handle in _handles)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception)
            {
                // TODO: Diagnostics.Record handle dispose failure.
            }
        }
    }
}
