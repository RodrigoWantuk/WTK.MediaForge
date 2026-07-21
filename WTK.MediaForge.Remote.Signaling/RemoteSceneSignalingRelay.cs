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
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public RemoteSceneSignalingRelay(RemoteSceneSignalingOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _maximumMessageBytes = options.MaximumSignalingMessageBytes;
        _queueCapacity = options.OutboundQueueCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(
        RemoteSceneSessionAccess access,
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(socket);
        await using var connection = new PeerConnection(access.Role, socket, _queueCapacity);
        var remainingLifetime = access.ExpiresAt - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Remote Scene signaling access has expired.");

        var peers = _sessions.GetOrAdd(access.SessionId, _ => new SessionPeers(_queueCapacity));
        peers.Attach(connection);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(remainingLifetime);
        var sender = connection.SendQueuedAsync(linkedCancellation.Token);
        try
        {
            await ReceiveAndRelayAsync(peers, connection, linkedCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            peers.Detach(connection);
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
                    WebSocketCloseStatus.NormalClosure,
                    "Signaling session closed.",
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
            peers.Forward(connection.Role, payload);
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

    private sealed class SessionPeers(int queueCapacity)
    {
        private readonly object _gate = new();
        private readonly Dictionary<RemoteScenePeerRole, PeerConnection> _connections = [];
        private readonly Dictionary<RemoteScenePeerRole, Queue<byte[]>> _pending = [];

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
                    connection.Enqueue(payload);
            }
        }

        public void Forward(RemoteScenePeerRole senderRole, byte[] payload)
        {
            var receiverRole = senderRole == RemoteScenePeerRole.Publisher
                ? RemoteScenePeerRole.Subscriber
                : RemoteScenePeerRole.Publisher;

            lock (_gate)
            {
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

                if (pending.Count >= queueCapacity)
                    throw new InvalidOperationException("Remote Scene signaling pending queue is full.");
                pending.Enqueue(payload);
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

        public PeerConnection(RemoteScenePeerRole role, WebSocket socket, int queueCapacity)
        {
            Role = role;
            Socket = socket;
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
            if (!_outbound.Writer.TryWrite(payload))
                throw new InvalidOperationException("Remote Scene signaling outbound queue is full.");
        }

        public async Task SendQueuedAsync(CancellationToken cancellationToken)
        {
            await foreach (var payload in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
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
