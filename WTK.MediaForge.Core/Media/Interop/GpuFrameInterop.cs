using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Core.Media.Audit;

namespace WTK.MediaForge.Core.Media.Interop;

public sealed class GpuExternalFrameDescriptor
{
    public required GpuVideoFrameDescriptor Frame { get; init; }

    public string BackendKind { get; init; } = "Unknown";
}

public sealed class HardwareEncoderInputRequirement
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required string PixelFormat { get; init; }

    public bool RequiresGpuSurface { get; init; } = true;
}

public sealed class HardwareEncoderInputLease : IDisposable
{
    private int _disposed;
    private readonly Action? _onRelease;

    private HardwareEncoderInputLease(
        GpuVideoFrameDescriptor descriptor,
        Action? onRelease,
        object? backendSurface)
    {
        Descriptor = descriptor;
        _onRelease = onRelease;
        BackendSurface = backendSurface;
    }

    public GpuVideoFrameDescriptor Descriptor { get; }

    internal object? BackendSurface { get; }

    public static HardwareEncoderInputLease Create(GpuVideoFrameDescriptor descriptor, Action? onRelease = null) =>
        new(descriptor, onRelease, backendSurface: null);

    internal static HardwareEncoderInputLease CreateWithBackendSurface(
        GpuVideoFrameDescriptor descriptor,
        object backendSurface,
        Action? onRelease = null) =>
        new(descriptor, onRelease, backendSurface);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _onRelease?.Invoke();
    }
}

public interface IGpuFrameImporter
{
    bool CanImport(GpuExternalFrameDescriptor descriptor);

    ValueTask<GpuVideoFrameLease> ImportAsync(
        GpuExternalFrameDescriptor descriptor,
        CancellationToken cancellationToken = default);
}

public interface IGpuFrameExporter
{
    bool CanExport(GpuVideoFrameDescriptor descriptor, HardwareEncoderInputRequirement requirement);

    ValueTask<HardwareEncoderInputLease> ExportForEncoderAsync(
        GpuVideoFrameDescriptor descriptor,
        IMediaTransportAuditSink auditSink,
        CancellationToken cancellationToken = default);
}
