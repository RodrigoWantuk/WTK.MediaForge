using System.Runtime.ExceptionServices;
using WTK.MediaForge.Composition.Runtime.Rendering;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// Experimental GPU preview sink. Presents completed Vulkan output surfaces to a Win32 panel.
/// Not a final product API until presenter lifecycle is fully hardened.
/// </summary>
public sealed class PreviewPanelSink : IRenderOutputSink
{
    private readonly nint _panelHandle;
    private readonly Func<RenderOutputFrameLease, CancellationToken, ValueTask>? _onFramePresented;
    private int _started;
    private int _disposed;

    public PreviewPanelSink(
        nint panelHandle,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameLease, CancellationToken, ValueTask>? onFramePresented = null)
        : this(RenderOutputSinkId.New(), panelHandle, backpressureMode, onFramePresented)
    {
    }

    public PreviewPanelSink(
        RenderOutputSinkId id,
        nint panelHandle,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameLease, CancellationToken, ValueTask>? onFramePresented = null)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Sink id cannot be empty.", nameof(id));

        if (panelHandle == 0)
            throw new ArgumentException("Panel handle cannot be zero.", nameof(panelHandle));

        Id = id;
        _panelHandle = panelHandle;
        BackpressureMode = backpressureMode;
        _onFramePresented = onFramePresented;
    }

    public RenderOutputSinkId Id { get; }

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Preview;

    public RenderOutputSinkBackpressureMode BackpressureMode { get; }

    public nint PanelHandle => _panelHandle;

    public void NotifyPanelClientSizeChanged(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        PreviewPanelClientSizeTracker.NotifyClientSize(_panelHandle, (uint)width, (uint)height);
    }

    public ValueTask StartAsync(
        RenderOutputSinkContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (context.BackendKind != RenderBackendKind.Vulkan)
        {
            throw new NotSupportedException(
                $"PreviewPanelSink requires Vulkan output frames. Backend '{context.BackendKind}' is not supported.");
        }

        Interlocked.Exchange(ref _started, 1);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnFrameAsync(
        RenderOutputFrameLease frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Exception? operationError = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (Volatile.Read(ref _started) == 0)
                throw new InvalidOperationException("PreviewPanelSink must be started before receiving frames.");

            if (frame.SurfaceLease is not IPreviewPresentableRenderedOutputSurfaceLease presentableSurface)
            {
                throw new NotSupportedException(
                    $"Render backend '{frame.BackendKind}' does not expose GPU preview presentation for output frames.");
            }

            await presentableSurface
                .PresentToWin32PanelAsync(_panelHandle, cancellationToken)
                .ConfigureAwait(false);

            if (_onFramePresented is not null)
                await _onFramePresented(frame, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            operationError = ex;
        }

        await DisposeFrameLeaseAsync(frame, operationError).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
            return ValueTask.CompletedTask;

        Interlocked.Exchange(ref _started, 0);
        PreviewPanelPresenterLifecycle.RemovePresentersForPanel(_panelHandle);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        Interlocked.Exchange(ref _started, 0);
        PreviewPanelPresenterLifecycle.RemovePresentersForPanel(_panelHandle);
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(PreviewPanelSink));
    }

    private static async ValueTask DisposeFrameLeaseAsync(
        RenderOutputFrameLease frame,
        Exception? operationError)
    {
        try
        {
            await frame.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception disposeError)
        {
            if (operationError is not null)
            {
                throw new AggregateException(
                    "PreviewPanelSink failed while consuming a frame and releasing its lease.",
                    operationError,
                    disposeError);
            }

            throw;
        }

        if (operationError is not null)
            ExceptionDispatchInfo.Capture(operationError).Throw();
    }
}
