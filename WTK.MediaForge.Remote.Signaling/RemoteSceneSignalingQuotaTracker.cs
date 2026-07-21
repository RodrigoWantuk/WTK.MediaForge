namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingQuotaTracker
{
    private readonly object _gate = new();
    private readonly RemoteSceneSignalingOptions _options;
    private readonly Dictionary<Guid, SessionQuota> _sessions = [];
    private readonly Dictionary<string, Queue<DateTimeOffset>> _creationTimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _webSockets = new(StringComparer.Ordinal);

    public RemoteSceneSignalingQuotaTracker(RemoteSceneSignalingOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public void RegisterInvitation(Guid sessionId, string tenantId, string userId, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            Prune(now);
            var identity = ValidateIdentity(tenantId, userId);
            if (_sessions.Count >= _options.MaximumActiveSessions)
                throw new RemoteSceneQuotaExceededException("Global active session quota exceeded.");
            if (_sessions.Values.Count(item => item.TenantId == identity.TenantId) >= _options.MaximumActiveSessionsPerTenant)
                throw new RemoteSceneQuotaExceededException("Tenant active session quota exceeded.");
            if (_sessions.Values.Count(item => item.Pending) >= _options.MaximumPendingInvitations)
                throw new RemoteSceneQuotaExceededException("Pending invitation quota exceeded.");
            if (!_creationTimes.TryGetValue(identity.Key, out var creations))
                _creationTimes[identity.Key] = creations = new Queue<DateTimeOffset>();
            while (creations.TryPeek(out var created) && now - created >= TimeSpan.FromMinutes(1))
                creations.Dequeue();
            if (creations.Count >= _options.MaximumInvitationCreationsPerMinutePerUser)
                throw new RemoteSceneQuotaExceededException("Invitation creation rate exceeded.");
            creations.Enqueue(now);
            _sessions.Add(sessionId, new SessionQuota(identity.TenantId, identity.UserId, expiresAt, Pending: true));
        }
    }

    public void RemoveSession(Guid sessionId)
    {
        lock (_gate)
            _sessions.Remove(sessionId);
    }

    public void MarkRedeemed(Guid sessionId)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var quota))
                _sessions[sessionId] = quota with { Pending = false };
        }
    }

    public IDisposable AcquireWebSocket(RemoteSceneSessionAccess access, DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);
            var key = $"{access.TenantId}\0{access.UserId}";
            _webSockets.TryGetValue(key, out var count);
            if (count >= _options.MaximumWebSocketsPerUser)
                throw new RemoteSceneQuotaExceededException("User WebSocket quota exceeded.");
            _webSockets[key] = count + 1;
            return new Release(() => ReleaseWebSocket(key));
        }
    }

    private void ReleaseWebSocket(string key)
    {
        lock (_gate)
        {
            if (!_webSockets.TryGetValue(key, out var count) || count <= 1)
                _webSockets.Remove(key);
            else
                _webSockets[key] = count - 1;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _sessions.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
            _sessions.Remove(id);
    }

    private static (string TenantId, string UserId, string Key) ValidateIdentity(string tenantId, string userId)
    {
        tenantId = tenantId?.Trim() ?? string.Empty;
        userId = userId?.Trim() ?? string.Empty;
        if (tenantId.Length is < 1 or > 128 || userId.Length is < 1 or > 128)
            throw new ArgumentException("Tenant and user ids must contain between 1 and 128 characters.");
        return (tenantId, userId, $"{tenantId}\0{userId}");
    }

    private sealed record SessionQuota(string TenantId, string UserId, DateTimeOffset ExpiresAt, bool Pending);

    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

public sealed class RemoteSceneQuotaExceededException(string message) : InvalidOperationException(message);
