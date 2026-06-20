using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.Gpu;

public sealed class D3D11GpuFrameSlotRing : IRetiredGpuResource, IDisposable
{
    private readonly D3D11SharedTextureFrameHandle[] _handles;
    private readonly TaskCompletionSource _fullyDisposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
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

    public Task FullyDisposed => _fullyDisposedTcs.Task;

    public bool IsFullyDisposed => Volatile.Read(ref _disposed) != 0;

    private int _retired;

    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    public D3D11SharedTextureFrameHandle GetHandle(int slotIndex) => _handles[slotIndex];

    public void Retire()
    {
        Volatile.Write(ref _retired, 1);
        Ring.Stop();
    }

    public bool TryFinalizePhysicalResources()
    {
        if (_fullyDisposedTcs.Task.IsCompleted)
            return _fullyDisposedTcs.Task.IsCompletedSuccessfully;

        try
        {
            Ring.RequestFinalize();

            if (!Ring.IsFullyDisposed)
                return false;

            DisposeHandlesIfNeeded();
            Volatile.Write(ref _disposed, 1);
            _fullyDisposedTcs.TrySetResult();
            return true;
        }
        catch (Exception ex)
        {
            _fullyDisposedTcs.TrySetException(ex);
            throw;
        }
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
