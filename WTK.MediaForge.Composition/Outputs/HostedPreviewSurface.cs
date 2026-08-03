using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

/// <summary>Logical identity for a platform-hosted GPU preview surface.</summary>
public readonly record struct HostedPreviewSurfaceId(Guid Value)
{
    /// <summary>Creates a new logical surface identity.</summary>
    public static HostedPreviewSurfaceId New() => new(Guid.NewGuid());

    /// <summary>Gets whether this identity has no value.</summary>
    public bool IsEmpty => Value == Guid.Empty;
}

/// <summary>Lifecycle state of a hosted preview surface.</summary>
public enum HostedPreviewSurfaceState { Detached = 0, Attached = 1, Closed = 2 }

/// <summary>Logical-to-physical DPI scale reported by the platform host.</summary>
public readonly record struct HostedPreviewDpiScale
{
    /// <summary>Creates a DPI scale.</summary>
    public HostedPreviewDpiScale(float x, float y)
    {
        if (!float.IsFinite(x) || x <= 0) throw new ArgumentOutOfRangeException(nameof(x));
        if (!float.IsFinite(y) || y <= 0) throw new ArgumentOutOfRangeException(nameof(y));
        X = x; Y = y;
    }
    /// <summary>Gets unit scale.</summary>
    public static HostedPreviewDpiScale One { get; } = new(1, 1);
    /// <summary>Gets the horizontal scale.</summary>
    public float X { get; }
    /// <summary>Gets the vertical scale.</summary>
    public float Y { get; }
}

/// <summary>Describes an engine-owned surface attachment.</summary>
public sealed record HostedPreviewAttachRequest
{
    /// <summary>Creates an attachment request.</summary>
    public HostedPreviewAttachRequest(RenderOutputId outputId, TimeSpan timeout)
    {
        if (outputId.IsEmpty) throw new ArgumentException("Output id cannot be empty.", nameof(outputId));
        ValidateTimeout(timeout, nameof(timeout));
        OutputId = outputId;
        Timeout = timeout;
    }
    /// <summary>Gets the output to attach.</summary>
    public RenderOutputId OutputId { get; }
    /// <summary>Gets the operation timeout.</summary>
    public TimeSpan Timeout { get; }
    /// <summary>Validates a bounded lifecycle timeout.</summary>
    internal static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(parameterName, "Timeout must be positive.");
    }

}

/// <summary>Describes a physical preview resize request.</summary>
public sealed record HostedPreviewResizeRequest
{
    /// <summary>Creates a resize request.</summary>
    public HostedPreviewResizeRequest(FrameSize size, HostedPreviewDpiScale dpiScale, TimeSpan timeout)
    {
        if (size.Width == 0 || size.Height == 0) throw new ArgumentOutOfRangeException(nameof(size));
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Size = size; DpiScale = dpiScale; Timeout = timeout;
    }
    /// <summary>Gets the physical size in pixels.</summary>
    public FrameSize Size { get; }
    /// <summary>Gets the DPI scale.</summary>
    public HostedPreviewDpiScale DpiScale { get; }
    /// <summary>Gets the operation timeout.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>Describes a native-host replacement request.</summary>
public sealed record HostedPreviewRebindRequest
{
    /// <summary>Creates a rebind request.</summary>
    public HostedPreviewRebindRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }
    /// <summary>Gets the operation timeout.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>Describes a detach request.</summary>
public sealed record HostedPreviewDetachRequest
{
    /// <summary>Creates a detach request.</summary>
    public HostedPreviewDetachRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }
    /// <summary>Gets the operation timeout.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>Describes a terminal close request.</summary>
public sealed record HostedPreviewCloseRequest
{
    /// <summary>Creates a close request.</summary>
    public HostedPreviewCloseRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }
    /// <summary>Gets the operation timeout.</summary>
    public TimeSpan Timeout { get; }
}

internal interface IHostedPreviewSurfaceAttachmentController
{
    ValueTask ResizeAsync(HostedPreviewResizeRequest request, CancellationToken cancellationToken);
    ValueTask RebindAsync(HostedPreviewRebindRequest request, CancellationToken cancellationToken);
    ValueTask DetachAsync(HostedPreviewDetachRequest request, CancellationToken cancellationToken);
    ValueTask CloseAsync(HostedPreviewCloseRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Platform-neutral lifecycle for a native GPU preview host. A timeout preserves
/// the pending platform operation and serializes later mutations until it settles.
/// </summary>
public abstract class HostedPreviewSurface : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _pendingOperation;
    private IHostedPreviewSurfaceAttachmentController? _attachmentController;
    private int _disposeRequested;

    /// <summary>Initializes a surface with a stable logical identity.</summary>
    protected HostedPreviewSurface(HostedPreviewSurfaceId id)
    {
        if (id.IsEmpty) throw new ArgumentException("Hosted preview surface id cannot be empty.", nameof(id));
        Id = id;
    }

    /// <summary>Gets the logical surface identity.</summary>
    public HostedPreviewSurfaceId Id { get; }
    /// <summary>Gets the current lifecycle state.</summary>
    public HostedPreviewSurfaceState State { get; private set; }
    /// <summary>Gets the output currently attached by the engine.</summary>
    public RenderOutputId? AttachedOutputId { get; private set; }
    /// <summary>Gets the last successfully applied physical pixel size.</summary>
    public FrameSize? Size { get; private set; }
    /// <summary>Gets the last successfully applied DPI scale.</summary>
    public HostedPreviewDpiScale DpiScale { get; private set; } = HostedPreviewDpiScale.One;

    /// <summary>Attaches this surface to an output.</summary>
    public ValueTask AttachAsync(HostedPreviewAttachRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(request, cancellationToken, "Hosted preview attach timed out.",
            token => AttachCoreAsync(request, token), () => { AttachedOutputId = request.OutputId; State = HostedPreviewSurfaceState.Attached; },
            () => ThrowIfClosedOrDisposing(), requireAttached: false);

    /// <summary>Applies a physical pixel-size and DPI update.</summary>
    public ValueTask ResizeAsync(HostedPreviewResizeRequest request, CancellationToken cancellationToken = default) =>
        _attachmentController is { } controller
            ? controller.ResizeAsync(request, cancellationToken)
            : ResizePhysicalAsync(request, cancellationToken);

    internal ValueTask ResizePhysicalAsync(HostedPreviewResizeRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(request, cancellationToken, "Hosted preview resize timed out.",
            token => ResizeCoreAsync(request, token), () => { Size = request.Size; DpiScale = request.DpiScale; }, EnsureAttached);

    /// <summary>Rebinds presentation to a replacement native host.</summary>
    public ValueTask RebindAsync(HostedPreviewRebindRequest request, CancellationToken cancellationToken = default) =>
        _attachmentController is { } controller
            ? controller.RebindAsync(request, cancellationToken)
            : RebindPhysicalAsync(request, cancellationToken);

    internal ValueTask RebindPhysicalAsync(HostedPreviewRebindRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(request, cancellationToken, "Hosted preview native-surface rebind timed out.",
            token => RebindCoreAsync(request, token), static () => { }, EnsureAttached);

    /// <summary>Detaches the surface. Repeated detach is a no-op.</summary>
    public async ValueTask DetachAsync(HostedPreviewDetachRequest request, CancellationToken cancellationToken = default)
    {
        if (_attachmentController is { } controller)
        {
            await controller.DetachAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }
        await DetachPhysicalAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask DetachPhysicalAsync(HostedPreviewDetachRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State is HostedPreviewSurfaceState.Detached or HostedPreviewSurfaceState.Closed) return;
        await ExecuteAsync(request, cancellationToken, "Hosted preview detach timed out.",
            token => DetachCoreAsync(request, token), () => { AttachedOutputId = null; State = HostedPreviewSurfaceState.Detached; },
            ThrowIfClosed).ConfigureAwait(false);
    }

    /// <summary>Closes the platform surface. Repeated close is a no-op.</summary>
    public async ValueTask CloseAsync(HostedPreviewCloseRequest request, CancellationToken cancellationToken = default)
    {
        if (_attachmentController is { } controller)
        {
            await controller.CloseAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }
        await ClosePhysicalAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask ClosePhysicalAsync(HostedPreviewCloseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (State == HostedPreviewSurfaceState.Closed) return;
        await ExecuteAsync(request, cancellationToken, "Hosted preview close timed out.",
            token => CloseCoreAsync(request, token), () => { AttachedOutputId = null; State = HostedPreviewSurfaceState.Closed; },
            ThrowIfClosed).ConfigureAwait(false);
    }

    /// <summary>Closes the surface once and is safe to invoke repeatedly.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
        if (_attachmentController is { } controller)
            await controller.CloseAsync(new HostedPreviewCloseRequest(TimeSpan.FromSeconds(5)), CancellationToken.None).ConfigureAwait(false);
        else
            await ClosePhysicalAsync(new HostedPreviewCloseRequest(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    internal RenderOutputTarget CreateRenderOutputTarget()
    {
        ThrowIfClosedOrDisposing();
        return CreateRenderOutputTargetCore();
    }

    internal void SetAttachmentController(IHostedPreviewSurfaceAttachmentController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (_attachmentController is not null)
            throw new InvalidOperationException("The hosted preview surface already has an engine attachment controller.");
        _attachmentController = controller;
    }

    internal void ClearAttachmentController(IHostedPreviewSurfaceAttachmentController controller)
    {
        if (ReferenceEquals(_attachmentController, controller))
            _attachmentController = null;
    }

    /// <summary>Creates the platform-private render target for the current host.</summary>
    protected abstract RenderOutputTarget CreateRenderOutputTargetCore();
    protected virtual ValueTask AttachCoreAsync(HostedPreviewAttachRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask ResizeCoreAsync(HostedPreviewResizeRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask RebindCoreAsync(HostedPreviewRebindRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask DetachCoreAsync(HostedPreviewDetachRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    protected virtual ValueTask CloseCoreAsync(HostedPreviewCloseRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    private async ValueTask ExecuteAsync<TRequest>(TRequest request, CancellationToken cancellationToken, string timeoutMessage,
        Func<CancellationToken, ValueTask> operation, Action commit, Action precondition, bool requireAttached = true)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        precondition();
        if (!requireAttached && State == HostedPreviewSurfaceState.Attached)
        {
            _gate.Release();
            throw new InvalidOperationException($"Hosted preview surface {Id.Value} is already attached.");
        }

        Task operationTask;
        try { operationTask = operation(CancellationToken.None).AsTask(); }
        catch (Exception exception) { _gate.Release(); throw new InvalidOperationException("Hosted preview platform operation failed before it could be scheduled.", exception); }

        _pendingOperation = operationTask;
        try
        {
            await operationTask.WaitAsync(GetTimeout(request), cancellationToken).ConfigureAwait(false);
            commit();
            _pendingOperation = null;
            _gate.Release();
        }
        catch (TimeoutException)
        {
            ObservePendingOperation(operationTask, commit);
            throw new TimeoutException(timeoutMessage);
        }
        catch
        {
            _pendingOperation = null;
            _gate.Release();
            throw;
        }
    }

    private static TimeSpan GetTimeout<TRequest>(TRequest request) where TRequest : class => request switch
    {
        HostedPreviewAttachRequest attach => attach.Timeout,
        HostedPreviewResizeRequest resize => resize.Timeout,
        HostedPreviewRebindRequest rebind => rebind.Timeout,
        HostedPreviewDetachRequest detach => detach.Timeout,
        HostedPreviewCloseRequest close => close.Timeout,
        _ => throw new ArgumentOutOfRangeException(nameof(request))
    };

    private void ObservePendingOperation(Task operationTask, Action commit) =>
        _ = operationTask.ContinueWith(completed =>
        {
            try { if (completed.IsCompletedSuccessfully) commit(); }
            finally { _pendingOperation = null; _gate.Release(); }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void EnsureAttached()
    {
        ThrowIfClosedOrDisposing();
        if (State != HostedPreviewSurfaceState.Attached) throw new InvalidOperationException($"Hosted preview surface {Id.Value} is not attached.");
    }

    private void ThrowIfClosedOrDisposing()
    {
        if (State == HostedPreviewSurfaceState.Closed || Volatile.Read(ref _disposeRequested) != 0)
            throw new ObjectDisposedException(nameof(HostedPreviewSurface));
    }

    private void ThrowIfClosed()
    {
        if (State == HostedPreviewSurfaceState.Closed)
            throw new ObjectDisposedException(nameof(HostedPreviewSurface));
    }
}
