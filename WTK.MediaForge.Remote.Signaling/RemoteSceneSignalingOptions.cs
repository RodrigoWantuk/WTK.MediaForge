namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneSignalingOptions
{
    public const string SectionName = "RemoteSceneSignaling";

    public string DatabasePath { get; set; } = "remote-scene-signaling.db";

    public string AdminBearerToken { get; set; } = string.Empty;

    public TimeSpan DefaultInvitationTtl { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan MaximumInvitationTtl { get; set; } = TimeSpan.FromHours(1);

    public bool AllowInsecureDevelopmentTransport { get; set; }

    public int MaximumSignalingMessageBytes { get; set; } = 64 * 1024;

    public int OutboundQueueCapacity { get; set; } = 128;

    public string[] TurnUrls { get; set; } = [];

    public string TurnSharedSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
            throw new InvalidOperationException("Remote Scene signaling requires a SQLite database path.");
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
        if ((TurnUrls.Length == 0) != string.IsNullOrWhiteSpace(TurnSharedSecret))
            throw new InvalidOperationException("TURN URLs and shared secret must be configured together.");
    }
}
