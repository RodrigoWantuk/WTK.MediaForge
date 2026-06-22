using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

internal sealed class VulkanRenderedOutputSurfaceLease : IRenderedOutputSurfaceLease
    , ICpuReadableRenderedOutputSurfaceLease
{
    private readonly VulkanOffscreenTargetHandle _targetHandle;
    private int _disposed;

    public VulkanRenderedOutputSurfaceLease(
        VulkanOffscreenTargetHandle targetHandle,
        RenderOutputId outputId,
        FrameSize size,
        RenderPixelFormat format = RenderPixelFormat.Rgba8Unorm)
    {
        _targetHandle = targetHandle ?? throw new ArgumentNullException(nameof(targetHandle));
        _targetHandle.RetainForSubmission();
        OutputId = outputId;
        Size = size;
        Format = format;
    }

    public RenderOutputId OutputId { get; }

    public FrameSize Size { get; }

    public RenderPixelFormat Format { get; }

    public RenderBackendKind BackendKind => RenderBackendKind.Vulkan;

    public object? BackendSurface => _targetHandle.Target;

    public ValueTask<CpuReadbackFrame> ReadCpuFrameAsync(
        RenderOutputFrameInfo info,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_targetHandle.Target is not VulkanOffscreenRenderTarget target)
        {
            throw new NotSupportedException(
                "Vulkan rendered output surface does not support CPU readback for this target type.");
        }

        var readback = VulkanOffscreenReadback.ReadFrame(target, cancellationToken);
        return ValueTask.FromResult(new CpuReadbackFrame(
            info,
            readback.StrideBytes,
            readback.Pixels));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _targetHandle.ReleaseSubmissionReference();

        return ValueTask.CompletedTask;
    }
}
