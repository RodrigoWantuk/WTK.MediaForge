using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingRelay
{
    private readonly ConcurrentDictionary<Guid, SessionPeers> _sessions = new();
    private readonly int _maximumMessageBytes;
    private readonly int _queueCapacity;
    private readonly int _maximumQueuedBytesPerPeer;
    private readonly int _maximumQueuedBytesPerSession;
    private readonly int _maximumMessagesPerMinute;
    private readonly TimeProvider _timeProvider;
    private readonly RemoteSceneSignalingQuotaTracker _quotas;
    private readonly RemoteSceneSignalingTelemetry _telemetry;
    private readonly ILogger<RemoteSceneSignalingRelay>? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RemoteSceneSignalingRelay(
        RemoteSceneSignalingOptions options,
        TimeProvider? timeProvider = null,
        RemoteSceneSignalingQuotaTracker? quotas = null,
        RemoteSceneSignalingTelemetry? telemetry = null,
        ILogger<RemoteSceneSignalingRelay>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _maximumMessageBytes = options.MaximumSignalingMessageBytes;
        _queueCapacity = options.OutboundQueueCapacity;
        _maximumQueuedBytesPerPeer = options.MaximumQueuedBytesPerPeer;
        _maximumQueuedBytesPerSession = options.MaximumQueuedBytesPerSession;
        _maximumMessagesPerMinute = options.MaximumMessagesPerMinutePerPeer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _quotas = quotas ?? new RemoteSceneSignalingQuotaTracker(options);
        _telemetry = telemetry ?? new RemoteSceneSignalingTelemetry();
        _logger = logger;
    }

    public async Task RunAsync(
        RemoteSceneSessionAccess access,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(socket);
        await using var connection = new PeerConnection(access.Role, socket, _queueCapacity, _maximumQueuedBytesPerPeer);
        var remainingLifetime = access.ExpiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Signaling access expired.", CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Remote Scene signaling access has expired.");
        }

        using var quotaLease = _quotas.AcquireWebSocket(access, _timeProvider.GetUtcNow());
        var peers = _sessions.GetOrAdd(access.SessionId, _ => new SessionPeers(_queueCapacity, _maximumQueuedBytesPerSession));
        peers.Attach(connection);
        _telemetry.ConnectionOpened(access.Role);
        _logger?.LogInformation("Signaling peer attached to session {SessionId} as {Role}.", access.SessionId, access.Role);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(remainingLifetime);
        var sender = connection.SendQueuedAsync(linkedCancellation.Token);
        WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure;
        var closeReason = "Signaling session closed.";
        try
        {
            await ReceiveAndRelayAsync(peers, connection, linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or RemoteSceneQuotaExceededException)
        {
            closeStatus = WebSocketCloseStatus.PolicyViolation;
            closeReason = "Signaling policy violation.";
            _telemetry.MessageRejected(exception.GetType().Name);
            _logger?.LogWarning("Signaling policy rejected session {SessionId} role {Role}: {Reason}", access.SessionId, access.Role, exception.Message);
            throw;
        }
        finally
        {
            peers.Detach(connection);
            _telemetry.ConnectionClosed(access.Role);
            linkedCancellation.Cancel();
            connection.Complete();
            try
            {
                await sender.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }

            if (peers.IsEmpty)
                _sessions.TryRemove(new KeyValuePair<Guid, SessionPeers>(access.SessionId, peers));

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    closeStatus,
                    closeReason,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task ReceiveAndRelayAsync(
        SessionPeers peers,
        PeerConnection connection,
        CancellationToken cancellationToken)
    {
        while (connection.Socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveMessageAsync(connection.Socket, cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return;

            RemoteSceneSignalingMessage message;
            try
            {
                message = JsonSerializer.Deserialize<RemoteSceneSignalingMessage>(payload, _jsonOptions)
                    ?? throw new JsonException("Signaling message was empty.");
            }
            catch (JsonException ex)
            {
                await connection.Socket.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Invalid signaling message.",
                    cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("Remote Scene signaling received invalid JSON.", ex);
            }

            ValidateMessage(message);
            connection.RecordMessage(_timeProvider.GetUtcNow(), _maximumMessagesPerMinute);
            peers.Forward(connection.Role, message, payload);
            _telemetry.MessageAccepted(connection.Role, message.Kind);
        }
    }

    private async Task<byte[]?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(Math.Min(_maximumMessageBytes, 16 * 1024));
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(rented, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.InvalidMessageType,
                        "Signaling accepts text messages only.",
                        cancellationToken).ConfigureAwait(false);
                    throw new InvalidDataException("Remote Scene signaling rejected a non-text WebSocket message.");
                }

                if (message.Length + result.Count > _maximumMessageBytes)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Signaling message exceeds the configured limit.",
                        cancellationToken).ConfigureAwait(false);
                    throw new InvalidDataException("Remote Scene signaling message exceeded the configured size limit.");
                }

                message.Write(rented, 0, result.Count);
                if (result.EndOfMessage)
                    return message.ToArray();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ValidateMessage(RemoteSceneSignalingMessage message)
    {
        if (!Enum.IsDefined(message.Kind))
            throw new InvalidDataException($"Unsupported signaling message kind '{message.Kind}'.");
        if (string.IsNullOrWhiteSpace(message.Payload))
            throw new InvalidDataException("Signaling message payload is required.");
        if (message.Sequence < 0)
            throw new InvalidDataException("Signaling message sequence cannot be negative.");
    }

    private sealed class SessionPeers
    {
        private readonly object _gate = new();
        private readonly Dictionary<RemoteScenePeerRole, PeerConnection> _connections = [];
        private readonly Dictionary<RemoteScenePeerRole, Queue<byte[]>> _pending = [];
        private readonly RemoteSceneSignalingProtocol _protocol = new();
        private readonly int _queueCapacity;
        private readonly int _maximumQueuedBytes;
        private int _pendingBytes;

        public SessionPeers(int queueCapacity, int maximumQueuedBytes)
        {
            _queueCapacity = queueCapacity;
            _maximumQueuedBytes = maximumQueuedBytes;
        }

        public bool IsEmpty
        {
            get
            {
                lock (_gate)
                    return _connections.Count == 0;
            }
        }

        public void Attach(PeerConnection connection)
        {
            lock (_gate)
            {
                if (_connections.ContainsKey(connection.Role))
                    throw new InvalidOperationException($"A {connection.Role} peer is already connected to this session.");

                _connections.Add(connection.Role, connection);
                if (!_pending.Remove(connection.Role, out var pending))
                    return;

                while (pending.TryDequeue(out var payload))
                {
                    connection.Enqueue(payload);
                    _pendingBytes -= payload.Length;
                }
            }
        }

        public void Forward(RemoteScenePeerRole senderRole, RemoteSceneSignalingMessage message, byte[] payload)
        {
            var receiverRole = senderRole == RemoteScenePeerRole.Publisher
                ? RemoteScenePeerRole.Subscriber
                : RemoteScenePeerRole.Publisher;

            lock (_gate)
            {
                if (!_protocol.Accept(senderRole, message))
                    return;
                if (_connections.TryGetValue(receiverRole, out var receiver))
                {
                    receiver.Enqueue(payload);
                    return;
                }

                if (!_pending.TryGetValue(receiverRole, out var pending))
                {
                    pending = new Queue<byte[]>();
                    _pending.Add(receiverRole, pending);
                }

                if (pending.Count >= _queueCapacity || _pendingBytes + payload.Length > _maximumQueuedBytes)
                    throw new InvalidOperationException("Remote Scene signaling pending queue is full.");
                pending.Enqueue(payload);
                _pendingBytes += payload.Length;
            }
        }

        public void Detach(PeerConnection connection)
        {
            lock (_gate)
            {
                if (_connections.TryGetValue(connection.Role, out var current) && ReferenceEquals(current, connection))
                    _connections.Remove(connection.Role);
            }
        }
    }

    private sealed class PeerConnection : IAsyncDisposable
    {
        private readonly Channel<byte[]> _outbound;
        private readonly int _maximumQueuedBytes;
        private readonly object _gate = new();
        private readonly Queue<DateTimeOffset> _messageTimes = [];
        private int _queuedBytes;

        public PeerConnection(RemoteScenePeerRole role, WebSocket socket, int queueCapacity, int maximumQueuedBytes)
        {
            Role = role;
            Socket = socket;
            _maximumQueuedBytes = maximumQueuedBytes;
            _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public RemoteScenePeerRole Role { get; }

        public WebSocket Socket { get; }

        public void Enqueue(byte[] payload)
        {
            lock (_gate)
            {
                if (_queuedBytes + payload.Length > _maximumQueuedBytes || !_outbound.Writer.TryWrite(payload))
                    throw new InvalidOperationException("Remote Scene signaling outbound queue is full.");
                _queuedBytes += payload.Length;
            }
        }

        public void RecordMessage(DateTimeOffset now, int maximumPerMinute)
        {
            lock (_gate)
            {
                while (_messageTimes.TryPeek(out var timestamp) && now - timestamp >= TimeSpan.FromMinutes(1))
                    _messageTimes.Dequeue();
                if (_messageTimes.Count >= maximumPerMinute)
                    throw new InvalidOperationException("Remote Scene signaling message rate exceeded.");
                _messageTimes.Enqueue(now);
            }
        }

        public async Task SendQueuedAsync(CancellationToken cancellationToken)
        {
            await foreach (var payload in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                    _queuedBytes -= payload.Length;
                await Socket.SendAsync(
                    payload,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public void Complete() => _outbound.Writer.TryComplete();

        public ValueTask DisposeAsync()
        {
            Complete();
            Socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
