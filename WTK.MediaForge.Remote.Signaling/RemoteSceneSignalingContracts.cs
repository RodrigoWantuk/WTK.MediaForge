using System.Text.Json.Serialization;
using WTK.MediaForge.Remote;

namespace WTK.MediaForge.Remote.Signaling;

public enum RemoteScenePeerRole
{
    Publisher,
    Subscriber
}

public enum RemoteSceneSignalingMessageKind
{
    Offer,
    Answer,
    IceCandidate,
    KeyFrameRequest,
    Renegotiate
}

public sealed record CreateRemoteSceneInvitationRequest
{
    public required string StreamName { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RemoteScenePeerRole OwnerRole { get; init; } = RemoteScenePeerRole.Publisher;

    public TimeSpan? TimeToLive { get; init; }

    public string TenantId { get; init; } = "default";

    public string UserId { get; init; } = "operator";
}

public sealed record CreateRemoteSceneInvitationResponse(
    Guid SessionId,
    string InvitationCode,
    string OwnerAccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<WebRtcIceServer> IceServers);

public sealed record RedeemRemoteSceneInvitationRequest(string InvitationCode);

public sealed record RedeemRemoteSceneInvitationResponse(
    Guid SessionId,
    string StreamName,
    RemoteScenePeerRole Role,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<WebRtcIceServer> IceServers);

public sealed record RemoteSceneSignalingMessage
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required RemoteSceneSignalingMessageKind Kind { get; init; }

    public required string Payload { get; init; }

    public long Sequence { get; init; }
}

public sealed record RemoteSceneSessionAccess(
    Guid SessionId,
    string StreamName,
    RemoteScenePeerRole Role,
    DateTimeOffset ExpiresAt,
    string TenantId = "default",
    string UserId = "anonymous");

public sealed record RemoteSceneInvitationRedemption(
    Guid SessionId,
    string StreamName,
    RemoteScenePeerRole Role,
    DateTimeOffset ExpiresAt,
    string TenantId = "default",
    string UserId = "anonymous");

public sealed record RemoteSceneStoredSession(
    Guid SessionId,
    string StreamName,
    RemoteScenePeerRole OwnerRole,
    byte[] InvitationCodeHash,
    byte[] OwnerTokenHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string TenantId = "default",
    string UserId = "operator");

public interface IRemoteSceneSessionStore
{
    Task CreateAsync(RemoteSceneStoredSession session, CancellationToken cancellationToken);

    Task<RemoteSceneInvitationRedemption?> RedeemAsync(
        byte[] invitationCodeHash,
        byte[] participantTokenHash,
        DateTimeOffset redeemedAt,
        CancellationToken cancellationToken);

    Task<RemoteSceneSessionAccess?> AuthorizeAsync(
        Guid sessionId,
        byte[] accessTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken);
}

public interface ITurnCredentialIssuer
{
    ValueTask<IReadOnlyList<WebRtcIceServer>> IssueAsync(
        string subject,
        TimeSpan lifetime,
        CancellationToken cancellationToken);
}
