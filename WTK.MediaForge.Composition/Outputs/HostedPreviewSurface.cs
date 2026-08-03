using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Composition.Outputs;

public readonly record struct HostedPreviewSurfaceId(Guid Value)
{
    public static HostedPreviewSurfaceId New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;
}

public enum HostedPreviewSurfaceState
{
    Detached = 0,
    Attached = 1,
    Closed = 2
}

public readonly record struct HostedPreviewDpiScale
{
    public HostedPreviewDpiScale(float x, float y)
    {
        if (!float.IsFinite(x) || x <= 0)
            throw new ArgumentOutOfRangeException(nameof(x), "DPI scale must be finite and positive.");
        if (!float.IsFinite(y) || y <= 0)
            throw new ArgumentOutOfRangeException(nameof(y), "DPI scale must be finite and positive.");

        X = x;
        Y = y;
    }

    public static HostedPreviewDpiScale One { get; } = new(1, 1);

    public float X { get; }

    public float Y { get; }
}

public sealed record HostedPreviewAttachRequest
{
    public HostedPreviewAttachRequest(RenderOutputId outputId, TimeSpan timeout)
    {
        if (outputId.IsEmpty)
            throw new ArgumentException("Output id cannot be empty.", nameof(outputId));

        ValidateTimeout(timeout, nameof(timeout));
        OutputId = outputId;
        Timeout = timeout;
    }

    public RenderOutputId OutputId { get; }

    public TimeSpan Timeout { get; }

    internal static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "Timeout must be positive.");
    }
}

public sealed record HostedPreviewResizeRequest
{
    public HostedPreviewResizeRequest(FrameSize size, HostedPreviewDpiScale dpiScale, TimeSpan timeout)
    {
        if (size.Width == 0 || size.Height == 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Preview size must be positive.");

        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Size = size;
        DpiScale = dpiScale;
        Timeout = timeout;
    }

    public FrameSize Size { get; }

    public HostedPreviewDpiScale DpiScale { get; }

    public TimeSpan Timeout { get; }
}

public sealed record HostedPreviewRebindRequest
{
    public HostedPreviewRebindRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public sealed record HostedPreviewDetachRequest
{
    public HostedPreviewDetachRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public sealed record HostedPreviewCloseRequest
{
    public HostedPreviewCloseRequest(TimeSpan timeout)
    {
        HostedPreviewAttachRequest.ValidateTimeout(timeout, nameof(timeout));
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}

public abstract class HostedPreviewSurface : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected HostedPreviewSurface(HostedPreviewSurfaceId id)
    {
        if (id.IsEmpty)
            throw new ArgumentException("Hosted preview surface id cannot be empty.", nameof(id));

        Id = id;
    }

    public HostedPreviewSurfaceId Id { get; }

    public HostedPreviewSurfaceState State { get; private set; }

    public RenderOutputId? AttachedOutputId { get; private set; }

    public FrameSize? Size { get; private set; }

    public HostedPreviewDpiScale DpiScale { get; private set; } = HostedPreviewDpiScale.One;

    public async ValueTask AttachAsync(
        HostedPreviewAttachRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            if (State == HostedPreviewSurfaceState.Attached)
                throw new InvalidOperationException($"Hosted preview surface {Id.Value} is already attached.");

            await WithTimeoutAsync(
                token => AttachCoreAsync(request, token),
                request.Timeout,
                "Hosted preview attach timed out.",
                cancellationToken).ConfigureAwait(false);

            AttachedOutputId = request.OutputId;
            State = HostedPreviewSurfaceState.Attached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ResizeAsync(
        HostedPreviewResizeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            await WithTimeoutAsync(
                token => ResizeCoreAsync(request, token),
                request.Timeout,
                "Hosted preview resize timed out.",
                cancellationToken).ConfigureAwait(false);

            Size = request.Size;
            DpiScale = request.DpiScale;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RebindAsync(
        HostedPreviewRebindRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            await WithTimeoutAsync(
                token => RebindCoreAsync(request, token),
                request.Timeout,
                "Hosted preview native-surface rebind timed out.",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DetachAsync(
        HostedPreviewDetachRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is HostedPreviewSurfaceState.Detached or HostedPreviewSurfaceState.Closed)
                return;

            await WithTimeoutAsync(
                token => DetachCoreAsync(request, token),
                request.Timeout,
                "Hosted preview detach timed out.",
                cancellationToken).ConfigureAwait(false);

            AttachedOutputId = null;
            State = HostedPreviewSurfaceState.Detached;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CloseAsync(
        HostedPreviewCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == HostedPreviewSurfaceState.Closed)
                return;

            await WithTimeoutAsync(
                token => CloseCoreAsync(request, token),
                request.Timeout,
                "Hosted preview close timed out.",
                cancellationToken).ConfigureAwait(false);

            AttachedOutputId = null;
            State = HostedPreviewSurfaceState.Closed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(new HostedPreviewCloseRequest(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal RenderOutputTarget CreateRenderOutputTarget()
    {
        ThrowIfClosed();
        return CreateRenderOutputTargetCore();
    }

    protected abstract RenderOutputTarget CreateRenderOutputTargetCore();

    protected virtual ValueTask AttachCoreAsync(
        HostedPreviewAttachRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask ResizeCoreAsync(
        HostedPreviewResizeRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask RebindCoreAsync(
        HostedPreviewRebindRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask DetachCoreAsync(
        HostedPreviewDetachRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    protected virtual ValueTask CloseCoreAsync(
        HostedPreviewCloseRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private void EnsureAttached()
    {
        ThrowIfClosed();
        if (State != HostedPreviewSurfaceState.Attached)
            throw new InvalidOperationException($"Hosted preview surface {Id.Value} is not attached.");
    }

    private void ThrowIfClosed()
    {
        if (State == HostedPreviewSurfaceState.Closed)
            throw new ObjectDisposedException(nameof(HostedPreviewSurface));
    }

    private static async ValueTask WithTimeoutAsync(
        Func<CancellationToken, ValueTask> operation,
        TimeSpan timeout,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await operation(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
