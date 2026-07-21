using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WTK.MediaForge.Remote.Signaling;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class RemoteSceneSignalingTests
{
    [Fact]
    public async Task Invitation_is_one_time_and_authorizes_owner_and_participant_roles()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var store = new SqliteRemoteSceneSessionStore(databasePath);
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
            var service = CreateService(store, clock);

            var invitation = await service.CreateAsync(
                new CreateRemoteSceneInvitationRequest
                {
                    StreamName = "Program",
                    OwnerRole = RemoteScenePeerRole.Publisher
                },
                CancellationToken.None);
            var owner = await service.AuthorizeAsync(
                invitation.SessionId,
                invitation.OwnerAccessToken,
                CancellationToken.None);
            var participant = await service.RedeemAsync(
                new RedeemRemoteSceneInvitationRequest(invitation.InvitationCode),
                CancellationToken.None);
            var duplicate = await service.RedeemAsync(
                new RedeemRemoteSceneInvitationRequest(invitation.InvitationCode),
                CancellationToken.None);
            var participantAccess = await service.AuthorizeAsync(
                invitation.SessionId,
                participant!.AccessToken,
                CancellationToken.None);

            Assert.Equal(RemoteScenePeerRole.Publisher, owner!.Role);
            Assert.Equal(RemoteScenePeerRole.Subscriber, participant.Role);
            Assert.Equal(RemoteScenePeerRole.Subscriber, participantAccess!.Role);
            Assert.Null(duplicate);

            await store.DisposeAsync();
            var databaseBytes = await File.ReadAllBytesAsync(databasePath);
            var databaseText = Encoding.Latin1.GetString(databaseBytes);
            Assert.DoesNotContain(invitation.InvitationCode, databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain(invitation.OwnerAccessToken, databaseText, StringComparison.Ordinal);
            Assert.DoesNotContain(participant.AccessToken, databaseText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Concurrent_invitation_redemption_has_exactly_one_winner()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var store = new SqliteRemoteSceneSessionStore(databasePath);
            var service = CreateService(
                store,
                new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero)));
            var invitation = await service.CreateAsync(
                new CreateRemoteSceneInvitationRequest { StreamName = "Program" },
                CancellationToken.None);

            var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                service.RedeemAsync(
                    new RedeemRemoteSceneInvitationRequest(invitation.InvitationCode),
                    CancellationToken.None)));

            Assert.Single(attempts, static result => result is not null);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Expired_invitation_cannot_be_redeemed_or_authorized_and_is_deleted()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var store = new SqliteRemoteSceneSessionStore(databasePath);
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
            var service = CreateService(store, clock);
            var invitation = await service.CreateAsync(
                new CreateRemoteSceneInvitationRequest
                {
                    StreamName = "Program",
                    TimeToLive = TimeSpan.FromMinutes(2)
                },
                CancellationToken.None);

            clock.Advance(TimeSpan.FromMinutes(3));

            Assert.Null(await service.RedeemAsync(
                new RedeemRemoteSceneInvitationRequest(invitation.InvitationCode),
                CancellationToken.None));
            Assert.Null(await service.AuthorizeAsync(
                invitation.SessionId,
                invitation.OwnerAccessToken,
                CancellationToken.None));
            Assert.Equal(1, await store.DeleteExpiredAsync(clock.GetUtcNow(), CancellationToken.None));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Turn_rest_credentials_use_expiring_username_and_hmac_sha1()
    {
        const string secret = "01234567890123456789012345678901";
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var issuer = new TurnRestCredentialIssuer(
            [new Uri("turns://turn.example.test:5349")],
            secret,
            clock);

        var servers = await issuer.IssueAsync("session-1", TimeSpan.FromMinutes(10), CancellationToken.None);

        var server = Assert.Single(servers);
        var username = $"{now.AddMinutes(10).ToUnixTimeSeconds()}:session-1";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var expectedCredential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));
        Assert.Equal(username, server.Username);
        Assert.Equal(expectedCredential, server.Credential);
        Assert.Equal("turns://turn.example.test:5349/", Assert.Single(server.Urls));
    }

    [Fact]
    public void Signaling_options_reject_partial_turn_and_weak_admin_configuration()
    {
        var options = CreateOptions();
        options.AdminBearerToken = "short";
        Assert.Throws<InvalidOperationException>(options.Validate);

        options = CreateOptions();
        options.TurnUrls = ["turn:turn.example.test"];
        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public async Task Invitation_rejects_unknown_owner_role()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var store = new SqliteRemoteSceneSessionStore(databasePath);
            var service = CreateService(
                store,
                new ManualTimeProvider(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero)));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CreateAsync(
                new CreateRemoteSceneInvitationRequest
                {
                    StreamName = "Program",
                    OwnerRole = (RemoteScenePeerRole)99
                },
                CancellationToken.None));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Signaling_relay_rejects_expired_access_and_disposes_socket()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var relay = new RemoteSceneSignalingRelay(CreateOptions(), clock);
        var socket = new ScriptedWebSocket();

        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.RunAsync(
            new RemoteSceneSessionAccess(
                Guid.NewGuid(),
                "Program",
                RemoteScenePeerRole.Publisher,
                now),
            socket,
            CancellationToken.None));

        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    [Fact]
    public async Task Signaling_relay_delivers_offer_queued_before_opposite_peer_connects()
    {
        var relay = new RemoteSceneSignalingRelay(CreateOptions());
        var sessionId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var publisherSocket = new ScriptedWebSocket();
        var subscriberSocket = new ScriptedWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var publisherTask = relay.RunAsync(
            new RemoteSceneSessionAccess(sessionId, "Program", RemoteScenePeerRole.Publisher, expiresAt),
            publisherSocket,
            timeout.Token);
        var offer = new RemoteSceneSignalingMessage
        {
            Kind = RemoteSceneSignalingMessageKind.Offer,
            Payload = "v=0\r\n",
            Sequence = 1
        };
        publisherSocket.EnqueueText(JsonSerializer.Serialize(offer, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var subscriberTask = relay.RunAsync(
            new RemoteSceneSessionAccess(sessionId, "Program", RemoteScenePeerRole.Subscriber, expiresAt),
            subscriberSocket,
            timeout.Token);
        var received = await subscriberSocket.ReadSentTextAsync(timeout.Token);

        var relayed = JsonSerializer.Deserialize<RemoteSceneSignalingMessage>(
            received,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(relayed);
        Assert.Equal(RemoteSceneSignalingMessageKind.Offer, relayed.Kind);
        Assert.Equal(offer.Payload, relayed.Payload);
        Assert.Equal(1, relayed.Sequence);

        publisherSocket.EnqueueClose();
        subscriberSocket.EnqueueClose();
        await Task.WhenAll(publisherTask, subscriberTask);
    }

    [Fact]
    public async Task Signaling_relay_queues_for_a_peer_that_disconnects_and_reconnects()
    {
        var relay = new RemoteSceneSignalingRelay(CreateOptions());
        var sessionId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var publisherSocket = new ScriptedWebSocket();
        var publisherTask = relay.RunAsync(
            new RemoteSceneSessionAccess(sessionId, "Program", RemoteScenePeerRole.Publisher, expiresAt),
            publisherSocket,
            timeout.Token);
        publisherSocket.EnqueueText(JsonSerializer.Serialize(
            Message(RemoteSceneSignalingMessageKind.Offer, 1),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var firstSubscriber = new ScriptedWebSocket();
        var firstTask = relay.RunAsync(
            new RemoteSceneSessionAccess(sessionId, "Program", RemoteScenePeerRole.Subscriber, expiresAt),
            firstSubscriber,
            timeout.Token);
        _ = await firstSubscriber.ReadSentTextAsync(timeout.Token);
        firstSubscriber.EnqueueClose();
        await firstTask;

        publisherSocket.EnqueueText(JsonSerializer.Serialize(
            Message(RemoteSceneSignalingMessageKind.IceCandidate, 2),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var secondSubscriber = new ScriptedWebSocket();
        var secondTask = relay.RunAsync(
            new RemoteSceneSessionAccess(sessionId, "Program", RemoteScenePeerRole.Subscriber, expiresAt),
            secondSubscriber,
            timeout.Token);

        var relayed = await secondSubscriber.ReadSentTextAsync(timeout.Token);
        Assert.Contains("iceCandidate", relayed, StringComparison.OrdinalIgnoreCase);
        publisherSocket.EnqueueClose();
        secondSubscriber.EnqueueClose();
        await Task.WhenAll(publisherTask, secondTask);
    }

    [Fact]
    public void Protocol_rejects_out_of_order_wrong_role_and_regressive_sequences()
    {
        var protocol = new RemoteSceneSignalingProtocol();
        Assert.Throws<InvalidDataException>(() => protocol.Accept(
            RemoteScenePeerRole.Subscriber,
            Message(RemoteSceneSignalingMessageKind.Answer, 1)));

        Assert.True(protocol.Accept(RemoteScenePeerRole.Publisher, Message(RemoteSceneSignalingMessageKind.Offer, 1)));
        Assert.False(protocol.Accept(RemoteScenePeerRole.Publisher, Message(RemoteSceneSignalingMessageKind.Offer, 1)));
        Assert.Throws<InvalidDataException>(() => protocol.Accept(
            RemoteScenePeerRole.Publisher,
            Message(RemoteSceneSignalingMessageKind.IceCandidate, 0)));
        Assert.Throws<InvalidDataException>(() => protocol.Accept(
            RemoteScenePeerRole.Publisher,
            Message(RemoteSceneSignalingMessageKind.Answer, 2)));
        Assert.True(protocol.Accept(RemoteScenePeerRole.Subscriber, Message(RemoteSceneSignalingMessageKind.Answer, 2)));
        Assert.True(protocol.Accept(RemoteScenePeerRole.Publisher, Message(RemoteSceneSignalingMessageKind.Renegotiate, 3)));
        Assert.True(protocol.Accept(RemoteScenePeerRole.Publisher, Message(RemoteSceneSignalingMessageKind.Offer, 4)));
    }

    [Fact]
    public async Task Signaling_relay_closes_with_policy_reason_when_pending_byte_queue_overflows()
    {
        var options = CreateOptions();
        options.MaximumSignalingMessageBytes = 1024;
        options.MaximumQueuedBytesPerPeer = 1024;
        options.MaximumQueuedBytesPerSession = 1024;
        var relay = new RemoteSceneSignalingRelay(options);
        var socket = new ScriptedWebSocket();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        socket.EnqueueText(JsonSerializer.Serialize(new RemoteSceneSignalingMessage
        {
            Kind = RemoteSceneSignalingMessageKind.Offer,
            Payload = new string('a', 600),
            Sequence = 1
        }, jsonOptions));
        socket.EnqueueText(JsonSerializer.Serialize(new RemoteSceneSignalingMessage
        {
            Kind = RemoteSceneSignalingMessageKind.IceCandidate,
            Payload = new string('b', 600),
            Sequence = 2
        }, jsonOptions));

        await Assert.ThrowsAsync<InvalidOperationException>(() => relay.RunAsync(
            new RemoteSceneSessionAccess(
                Guid.NewGuid(), "Program", RemoteScenePeerRole.Publisher, DateTimeOffset.UtcNow.AddMinutes(5)),
            socket,
            CancellationToken.None));

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
        Assert.Equal("Signaling policy violation.", socket.CloseStatusDescription);
    }

    [Fact]
    public async Task Revoked_session_token_is_rejected()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using var store = new SqliteRemoteSceneSessionStore(databasePath);
            var service = CreateService(store, new ManualTimeProvider(DateTimeOffset.UtcNow));
            var invitation = await service.CreateAsync(
                new CreateRemoteSceneInvitationRequest { StreamName = "Program" },
                CancellationToken.None);

            await service.RevokeAsync(invitation.SessionId, CancellationToken.None);

            Assert.Null(await service.AuthorizeAsync(
                invitation.SessionId,
                invitation.OwnerAccessToken,
                CancellationToken.None));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void Quotas_limit_global_tenant_websocket_and_invitation_attack_rates()
    {
        var options = CreateOptions();
        options.MaximumActiveSessions = 2;
        options.MaximumActiveSessionsPerTenant = 1;
        options.MaximumPendingInvitations = 2;
        options.MaximumWebSocketsPerUser = 1;
        options.MaximumInvitationCreationsPerMinutePerUser = 1;
        var quotas = new RemoteSceneSignalingQuotaTracker(options);
        var now = DateTimeOffset.UtcNow;
        quotas.RegisterInvitation(Guid.NewGuid(), "tenant-a", "user-a", now, now.AddMinutes(5));

        Assert.Throws<RemoteSceneQuotaExceededException>(() =>
            quotas.RegisterInvitation(Guid.NewGuid(), "tenant-a", "user-b", now, now.AddMinutes(5)));
        quotas.RegisterInvitation(Guid.NewGuid(), "tenant-b", "user-a", now, now.AddMinutes(5));
        Assert.Throws<RemoteSceneQuotaExceededException>(() =>
            quotas.RegisterInvitation(Guid.NewGuid(), "tenant-c", "user-c", now, now.AddMinutes(5)));

        var rateOptions = CreateOptions();
        rateOptions.MaximumInvitationCreationsPerMinutePerUser = 1;
        var rateQuotas = new RemoteSceneSignalingQuotaTracker(rateOptions);
        rateQuotas.RegisterInvitation(Guid.NewGuid(), "tenant-rate", "user-rate", now, now.AddMinutes(5));
        Assert.Throws<RemoteSceneQuotaExceededException>(() =>
            rateQuotas.RegisterInvitation(Guid.NewGuid(), "tenant-rate", "user-rate", now, now.AddMinutes(5)));

        var access = new RemoteSceneSessionAccess(
            Guid.NewGuid(), "Program", RemoteScenePeerRole.Publisher, now.AddMinutes(5), "tenant-a", "user-a");
        using var first = quotas.AcquireWebSocket(access, now);
        Assert.Throws<RemoteSceneQuotaExceededException>(() => quotas.AcquireWebSocket(access, now));
    }

    [Fact]
    public async Task Trusted_proxy_applies_external_https_and_distinct_effective_client_ips()
    {
        var options = CreateOptions();
        options.TrustedProxies = ["proxy.example.test"];
        Assert.Throws<InvalidOperationException>(options.Validate);

        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1,
            RequireHeaderSymmetry = true
        };
        forwarded.KnownNetworks.Clear();
        forwarded.KnownProxies.Clear();
        forwarded.KnownProxies.Add(System.Net.IPAddress.Parse("10.0.0.5"));
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(forwarded));
        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");
        first.Request.Headers["X-Forwarded-For"] = "198.51.100.10";
        first.Request.Headers["X-Forwarded-Proto"] = "https";
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");
        second.Request.Headers["X-Forwarded-For"] = "198.51.100.11";
        second.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.Invoke(first);
        await middleware.Invoke(second);

        Assert.True(first.Request.IsHttps);
        Assert.True(second.Request.IsHttps);
        Assert.NotEqual(first.Connection.RemoteIpAddress, second.Connection.RemoteIpAddress);
    }

    private static RemoteSceneSignalingMessage Message(RemoteSceneSignalingMessageKind kind, long sequence) =>
        new() { Kind = kind, Payload = $"payload-{kind}", Sequence = sequence };

    private static RemoteSceneInvitationService CreateService(
        IRemoteSceneSessionStore store,
        TimeProvider clock) =>
        new(store, NoTurnCredentialIssuer.Instance, CreateOptions(), clock);

    private static RemoteSceneSignalingOptions CreateOptions() =>
        new()
        {
            DatabasePath = "unused.db",
            AdminBearerToken = "01234567890123456789012345678901"
        };

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"wtk-mediaforge-signaling-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Channel<IncomingMessage> _incoming = Channel.CreateUnbounded<IncomingMessage>();
        private readonly Channel<byte[]> _sent = Channel.CreateUnbounded<byte[]>();
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override string? SubProtocol => null;

        public override WebSocketState State => _state;

        public void EnqueueText(string payload) =>
            _incoming.Writer.TryWrite(new IncomingMessage(
                Encoding.UTF8.GetBytes(payload),
                WebSocketMessageType.Text));

        public void EnqueueClose() =>
            _incoming.Writer.TryWrite(new IncomingMessage([], WebSocketMessageType.Close));

        public async Task<string> ReadSentTextAsync(CancellationToken cancellationToken) =>
            Encoding.UTF8.GetString(await _sent.Reader.ReadAsync(cancellationToken));

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
            _incoming.Writer.TryComplete();
            _sent.Writer.TryComplete();
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            var message = await _incoming.Reader.ReadAsync(cancellationToken);
            if (message.Type == WebSocketMessageType.Close)
            {
                _state = WebSocketState.CloseReceived;
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, endOfMessage: true);
            }

            message.Payload.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(message.Payload.Length, message.Type, endOfMessage: true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            if (messageType != WebSocketMessageType.Text || !endOfMessage)
                throw new InvalidOperationException("The signaling test socket accepts complete text messages only.");

            if (!_sent.Writer.TryWrite(buffer.ToArray()))
                throw new InvalidOperationException("The signaling test socket send queue is closed.");
            return Task.CompletedTask;
        }

        private sealed record IncomingMessage(byte[] Payload, WebSocketMessageType Type);
    }
}
