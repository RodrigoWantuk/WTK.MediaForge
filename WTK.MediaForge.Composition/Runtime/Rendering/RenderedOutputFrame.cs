using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Runtime.Rendering;

internal sealed class RenderedOutputFrame
{
    public RenderedOutputFrame(
        RenderOutputId outputId,
        FrameSize size,
        RenderPixelFormat format,
        RenderBackendKind backendKind)
        : this(
            outputId,
            size,
            format,
            backendKind,
            new NullRenderedOutputSurfaceLease(outputId, size, format, backendKind))
    {
    }

    public RenderedOutputFrame(
        RenderOutputId outputId,
        FrameSize size,
        RenderPixelFormat format,
        RenderBackendKind backendKind,
        IRenderedOutputSurfaceLease surfaceLease)
    {
        ArgumentNullException.ThrowIfNull(surfaceLease);

        if (surfaceLease.OutputId != outputId)
            throw new ArgumentException("Rendered output surface lease output id must match the frame output id.", nameof(surfaceLease));

        OutputId = outputId;
        Size = size;
        Format = format;
        BackendKind = backendKind;
        SurfaceLease = surfaceLease;
    }

    public RenderOutputId OutputId { get; }

    public FrameSize Size { get; }

    public RenderPixelFormat Format { get; }

    public RenderBackendKind BackendKind { get; }

    internal IRenderedOutputSurfaceLease SurfaceLease { get; }

    internal RenderOutputFrameLease CreateLease(
        RenderOutputFrameInfo info,
        Func<ValueTask>? onReleased)
    {
        ArgumentNullException.ThrowIfNull(info);
        Interlocked.Increment(ref _leaseCount);
        return new RenderOutputFrameLease(info, SurfaceLease, () => ReleaseLeaseAsync(onReleased));
    }

    private int _leaseCount;
    private int _surfaceReleased;

    private async ValueTask ReleaseLeaseAsync(Func<ValueTask>? onReleased)
    {
        var remaining = Interlocked.Decrement(ref _leaseCount);
        if (remaining < 0)
            throw new InvalidOperationException("Rendered output frame lease was released more times than it was acquired.");

        Exception? releaseError = null;

        if (remaining == 0)
            releaseError = await DisposeSurfaceAsync(releaseError).ConfigureAwait(false);

        if (onReleased is not null)
        {
            try
            {
                await onReleased().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                releaseError = releaseError is null
                    ? ex
                    : new AggregateException(releaseError, ex);
            }
        }

        if (releaseError is not null)
            throw releaseError;
    }

    internal async ValueTask DisposeSurfaceAsync()
    {
        var releaseError = await DisposeSurfaceAsync(releaseError: null).ConfigureAwait(false);
        if (releaseError is not null)
            throw releaseError;
    }

    private async ValueTask<Exception?> DisposeSurfaceAsync(Exception? releaseError)
    {
        if (Interlocked.Exchange(ref _surfaceReleased, 1) != 0)
            return releaseError;

        try
        {
            await SurfaceLease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            releaseError = releaseError is null
                ? ex
                : new AggregateException(releaseError, ex);
        }

        return releaseError;
    }
}
