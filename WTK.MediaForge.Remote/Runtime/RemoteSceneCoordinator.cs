namespace WTK.MediaForge.Remote;

/// <summary>
/// Serializes Remote Scene session, publisher, and subscriber ownership. Media work remains
/// in the transport; this coordinator only enforces one deterministic lifecycle path.
/// </summary>
public sealed class RemoteSceneCoordinator(IRemoteSceneTransport transport) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private IRemoteSceneSession? _session;
    private IRemoteScenePublisher? _publisher;
    private IRemoteSceneSubscriber? _subscriber;
    private int _disposed;

    public async Task ConnectAsync(RemoteSceneConnectionRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is not null) throw new InvalidOperationException("Remote Scene is already connected.");
            request.Connection.Validate();
            _session = await transport.ConnectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<IRemoteScenePublisher> StartPublishingAsync(RemoteScenePublishRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_publisher is not null) throw new InvalidOperationException("Remote Scene publishing is already active.");
            RemoteSceneRequestValidator.Validate(request);
            _publisher = await RequireSession().PublishAsync(request, cancellationToken).ConfigureAwait(false);
            return _publisher;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<IRemoteSceneSubscriber> StartSubscribingAsync(RemoteSceneSubscribeRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_subscriber is not null) throw new InvalidOperationException("Remote Scene subscription is already active.");
            RemoteSceneRequestValidator.Validate(request);
            _subscriber = await RequireSession().SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
            return _subscriber;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        var errors = new List<Exception>();
        try
        {
            await DisposeOwnedAsync(_publisher, errors).ConfigureAwait(false);
            _publisher = null;
            await DisposeOwnedAsync(_subscriber, errors).ConfigureAwait(false);
            _subscriber = null;
            await DisposeOwnedAsync(_session, errors).ConfigureAwait(false);
            _session = null;
            await DisposeOwnedAsync(transport, errors).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }

        if (errors.Count > 0) throw new AggregateException("Remote Scene lifecycle cleanup failed.", errors);
    }

    private IRemoteSceneSession RequireSession() => _session
        ?? throw new InvalidOperationException("Remote Scene must be connected first.");

    private static async ValueTask DisposeOwnedAsync(IAsyncDisposable? value, ICollection<Exception> errors)
    {
        if (value is null) return;
        try { await value.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { errors.Add(error); }
    }
}
