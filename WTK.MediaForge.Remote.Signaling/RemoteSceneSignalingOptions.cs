namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingOptions
{
    public const string SectionName = "RemoteSceneSignaling";

    public string DatabasePath { get; set; } = "remote-scene-signaling.db";

    public string InstanceId { get; set; } = string.Empty;

    public string AdminBearerToken { get; set; } = string.Empty;

    public TimeSpan DefaultInvitationTtl { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaximumInvitationTtl { get; set; } = TimeSpan.FromHours(1);

    public bool AllowInsecureDevelopmentTransport { get; set; }

    public int MaximumSignalingMessageBytes { get; set; } = 64 * 1024;

    public int OutboundQueueCapacity { get; set; } = 128;

    public int MaximumQueuedBytesPerPeer { get; set; } = 2 * 1024 * 1024;

    public int MaximumQueuedBytesPerSession { get; set; } = 4 * 1024 * 1024;

    public int MaximumMessagesPerMinutePerPeer { get; set; } = 600;

    public int MaximumActiveSessions { get; set; } = 1000;

    public int MaximumActiveSessionsPerTenant { get; set; } = 100;

    public int MaximumPendingInvitations { get; set; } = 500;

    public int MaximumWebSocketsPerUser { get; set; } = 4;

    public int MaximumInvitationCreationsPerMinutePerUser { get; set; } = 10;

    public string[] TrustedProxies { get; set; } = [];

    public bool ProtectHealthEndpoint { get; set; }

    public string HealthBearerToken { get; set; } = string.Empty;

    public string[] TurnUrls { get; set; } = [];

    public string TurnSharedSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
            throw new InvalidOperationException("Remote Scene signaling requires a SQLite database path.");
        if (string.IsNullOrWhiteSpace(InstanceId) || InstanceId.Length > 128)
            throw new InvalidOperationException("Remote Scene signaling requires an instance id of at most 128 characters.");
        if (AdminBearerToken.Length < 32)
            throw new InvalidOperationException("Remote Scene signaling admin bearer token must contain at least 32 characters.");
        if (DefaultInvitationTtl <= TimeSpan.Zero || DefaultInvitationTtl > MaximumInvitationTtl)
            throw new InvalidOperationException("Default invitation TTL must be positive and no greater than the maximum TTL.");
        if (MaximumInvitationTtl > TimeSpan.FromHours(24))
            throw new InvalidOperationException("Remote Scene invitation TTL cannot exceed 24 hours.");
        if (MaximumSignalingMessageBytes is < 1024 or > 1024 * 1024)
            throw new InvalidOperationException("Maximum signaling message size must be between 1 KiB and 1 MiB.");
        if (OutboundQueueCapacity is < 8 or > 4096)
            throw new InvalidOperationException("Outbound signaling queue capacity must be between 8 and 4096.");
        if (MaximumQueuedBytesPerPeer < MaximumSignalingMessageBytes ||
            MaximumQueuedBytesPerSession < MaximumQueuedBytesPerPeer)
            throw new InvalidOperationException("Signaling byte quotas must fit at least one message and the session quota must cover one peer quota.");
        if (MaximumMessagesPerMinutePerPeer <= 0 || MaximumActiveSessions <= 0 ||
            MaximumActiveSessionsPerTenant <= 0 || MaximumActiveSessionsPerTenant > MaximumActiveSessions ||
            MaximumPendingInvitations <= 0 || MaximumWebSocketsPerUser <= 0 ||
            MaximumInvitationCreationsPerMinutePerUser <= 0)
            throw new InvalidOperationException("Remote Scene signaling quotas must be positive and internally consistent.");
        if (TrustedProxies.Any(value => !System.Net.IPAddress.TryParse(value, out _)))
            throw new InvalidOperationException("Every trusted proxy must be an IP address.");
        if (ProtectHealthEndpoint && HealthBearerToken.Length < 32)
            throw new InvalidOperationException("Protected health endpoint bearer token must contain at least 32 characters.");
        if ((TurnUrls.Length == 0) != string.IsNullOrWhiteSpace(TurnSharedSecret))
            throw new InvalidOperationException("TURN URLs and shared secret must be configured together.");
    }
}
