using System.Runtime.ExceptionServices;
using WTK.MediaForge.Composition.Runtime.Rendering;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>
/// GPU preview sink. Presents completed Vulkan output surfaces to a Win32 panel
/// without CPU readback and owns presentation lifecycle/lease release.
/// </summary>
public sealed class PreviewPanelSink : IRenderOutputSink
{
    private static readonly TimeSpan DefaultDisposePresentationTimeout = TimeSpan.FromSeconds(5);

    private readonly nint _panelHandle;
    private readonly Func<RenderOutputFrameLease, CancellationToken, ValueTask>? _onFramePresented;
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private readonly SemaphoreSlim _presenterRemovalGate = new(1, 1);
    private readonly TimeSpan _disposePresentationTimeout;
    private int _started;
    private int _disposeRequested;
    private int _disposed;
    private int _presenterRemoved;

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
        : this(id, panelHandle, backpressureMode, onFramePresented, DefaultDisposePresentationTimeout)
    {
    }

    internal PreviewPanelSink(
        RenderOutputSinkId id,
        nint panelHandle,
        RenderOutputSinkBackpressureMode backpressureMode,
        Func<RenderOutputFrameLease, CancellationToken, ValueTask>? onFramePresented,
        TimeSpan disposePresentationTimeout)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Sink id cannot be empty.", nameof(id));

        if (panelHandle == 0)
            throw new ArgumentException("Panel handle cannot be zero.", nameof(panelHandle));

        if (disposePresentationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposePresentationTimeout),
                "Preview presentation dispose timeout must be positive.");
        }

        Id = id;
        _panelHandle = panelHandle;
        BackpressureMode = backpressureMode;
        _onFramePresented = onFramePresented;
        _disposePresentationTimeout = disposePresentationTimeout;
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
        Interlocked.Exchange(ref _presenterRemoved, 0);
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            Interlocked.Exchange(ref _started, 0);
            ThrowIfDisposed();
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask OnFrameAsync(
        RenderOutputFrameLease frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Exception? operationError = null;
        var presentationGateAcquired = false;

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

            await _presentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            presentationGateAcquired = true;

            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (Volatile.Read(ref _started) == 0)
                throw new InvalidOperationException("PreviewPanelSink must be started before receiving frames.");

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
        finally
        {
            if (presentationGateAcquired)
                _presentationGate.Release();
        }

        await DisposeFrameLeaseAsync(frame, operationError).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Interlocked.Exchange(ref _started, 0);
        await WaitForPresentationIdleAsync(cancellationToken).ConfigureAwait(false);
        await RemovePresenterOnceAsync(_disposePresentationTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        Interlocked.Exchange(ref _disposeRequested, 1);
        Interlocked.Exchange(ref _started, 0);

        using var timeout = new CancellationTokenSource(_disposePresentationTimeout);
        try
        {
            await WaitForPresentationIdleAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"PreviewPanelSink presentation did not become idle within {_disposePresentationTimeout}.",
                ex);
        }

        await RemovePresenterOnceAsync(_disposePresentationTimeout, CancellationToken.None).ConfigureAwait(false);
        Interlocked.Exchange(ref _disposed, 1);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _disposeRequested) != 0)
            throw new ObjectDisposedException(nameof(PreviewPanelSink));
    }

    private async ValueTask WaitForPresentationIdleAsync(CancellationToken cancellationToken)
    {
        await _presentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _presentationGate.Release();
    }

    private async ValueTask RemovePresenterOnceAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _presenterRemoved) != 0)
            return;

        await _presenterRemovalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _presenterRemoved) != 0)
                return;

            await PreviewPanelPresenterLifecycle
                .RemovePresentersForPanelAsync(_panelHandle, timeout, cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref _presenterRemoved, 1);
        }
        finally
        {
            _presenterRemovalGate.Release();
        }
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
