using Vortice.Direct3D11;
using Vortice.DXGI;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Gpu.Slots;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Capture.Gpu;

public sealed class D3D11GpuFrameSlotRing : IRetiredGpuResource, IDisposable
{
    private readonly D3D11GpuFrameSlot[] _slots;
    private readonly ID3D11GpuFrameSlotDisposer _slotDisposer;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly TaskCompletionSource _fullyDisposedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _handlesDisposed;
    private int _disposed;

    public D3D11GpuFrameSlotRing(
        ID3D11Device device,
        uint width,
        uint height,
        Format format = Format.B8G8R8A8_UNorm,
        int slotCount = 3,
        IMediaForgeDiagnosticsSink? diagnostics = null)
        : this(
            device,
            width,
            height,
            format,
            slotCount,
            diagnostics,
            DefaultD3D11GpuFrameSlotDisposer.Instance)
    {
    }

    internal D3D11GpuFrameSlotRing(
        ID3D11Device device,
        uint width,
        uint height,
        Format format,
        int slotCount,
        IMediaForgeDiagnosticsSink? diagnostics,
        ID3D11GpuFrameSlotDisposer slotDisposer)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(slotDisposer);

        _diagnostics = diagnostics;
        _slotDisposer = slotDisposer;
        Ring = new GpuFrameSlotRing(
            slotCount,
            reusePhysicalResources: true,
            onResourceDisposeFailed: (ex, slotIndex) =>
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "capture.slot_resource_dispose_failed",
                    "Failed to dispose GPU frame slot resource.",
                    nameof(D3D11GpuFrameSlotRing),
                    ex,
                    slotIndex: slotIndex));
        _slots = new D3D11GpuFrameSlot[slotCount];

        for (var i = 0; i < slotCount; i++)
        {
            var handle = D3D11SharedTextureFactory.CreateSharedTexture(device, width, height, format);
            _slots[i] = new D3D11GpuFrameSlot(i, handle);
            Ring.InitializeSlot(i, handle);
        }
    }

    public GpuFrameSlotRing Ring { get; }

    public Task FullyDisposed => _fullyDisposedTcs.Task;

    public bool IsFullyDisposed => Volatile.Read(ref _disposed) != 0;

    public bool IsRetired => _slots.Length > 0 && _slots[0].IsRetired;

    public D3D11GpuFrameSlot GetSlot(int slotIndex) => _slots[slotIndex];

    public D3D11SharedTextureFrameHandle GetHandle(int slotIndex) => _slots[slotIndex].Handle;

    public void Retire()
    {
        foreach (var slot in _slots)
            slot.MarkRetired();

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

        List<Exception>? errors = null;

        foreach (var slot in _slots)
        {
            try
            {
                _slotDisposer.DisposeSlot(slot);
            }
            catch (Exception ex)
            {
                errors ??= [];
                errors.Add(ex);

                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "capture.handle_dispose_failed",
                    "Failed to dispose D3D11 shared texture handle.",
                    nameof(D3D11GpuFrameSlotRing),
                    ex,
                    slotIndex: slot.SlotIndex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException(
                "Failed to dispose one or more D3D11 shared texture handles.",
                errors);
        }
    }
}
