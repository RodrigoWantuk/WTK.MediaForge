namespace WTK.MediaForge.Remote.Signaling;

public sealed class RemoteSceneInvitationService
{
    private readonly IRemoteSceneSessionStore _store;
    private readonly ITurnCredentialIssuer _turnCredentialIssuer;
    private readonly RemoteSceneSignalingOptions _options;
    private readonly TimeProvider _timeProvider;

    public RemoteSceneInvitationService(
        IRemoteSceneSessionStore store,
        ITurnCredentialIssuer turnCredentialIssuer,
        RemoteSceneSignalingOptions options,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _turnCredentialIssuer = turnCredentialIssuer ?? throw new ArgumentNullException(nameof(turnCredentialIssuer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateRemoteSceneInvitationResponse> CreateAsync(
        CreateRemoteSceneInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var streamName = ValidateStreamName(request.StreamName);
        if (!Enum.IsDefined(request.OwnerRole))
            throw new ArgumentOutOfRangeException(nameof(request), "Remote Scene owner role is invalid.");
        var ttl = request.TimeToLive ?? _options.DefaultInvitationTtl;
        if (ttl <= TimeSpan.Zero || ttl > _options.MaximumInvitationTtl)
            throw new ArgumentOutOfRangeException(nameof(request), "Invitation TTL is outside the configured range.");

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(ttl);
        var invitationCode = RemoteSceneSecret.Create(16);
        var ownerToken = RemoteSceneSecret.Create(32);
        var session = new RemoteSceneStoredSession(
            Guid.NewGuid(),
            streamName,
            request.OwnerRole,
            RemoteSceneSecret.Hash(invitationCode),
            RemoteSceneSecret.Hash(ownerToken),
            now,
            expiresAt);

        await _store.CreateAsync(session, cancellationToken).ConfigureAwait(false);
        var iceServers = await _turnCredentialIssuer
            .IssueAsync(session.SessionId.ToString("N"), ttl, cancellationToken)
            .ConfigureAwait(false);

        return new CreateRemoteSceneInvitationResponse(
            session.SessionId,
            invitationCode,
            ownerToken,
            expiresAt,
            iceServers);
    }

    public async Task<RedeemRemoteSceneInvitationResponse?> RedeemAsync(
        RedeemRemoteSceneInvitationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = _timeProvider.GetUtcNow();
        var participantToken = RemoteSceneSecret.Create(32);
        var redemption = await _store.RedeemAsync(
            RemoteSceneSecret.Hash(request.InvitationCode),
            RemoteSceneSecret.Hash(participantToken),
            now,
            cancellationToken).ConfigureAwait(false);

        if (redemption is null)
            return null;

        var lifetime = redemption.ExpiresAt - now;
        var iceServers = await _turnCredentialIssuer
            .IssueAsync(redemption.SessionId.ToString("N"), lifetime, cancellationToken)
            .ConfigureAwait(false);
        return new RedeemRemoteSceneInvitationResponse(
            redemption.SessionId,
            redemption.StreamName,
            redemption.Role,
            participantToken,
            redemption.ExpiresAt,
            iceServers);
    }

    public Task<RemoteSceneSessionAccess?> AuthorizeAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken) =>
        _store.AuthorizeAsync(
            sessionId,
            RemoteSceneSecret.Hash(accessToken),
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private static string ValidateStreamName(string streamName)
    {
        if (string.IsNullOrWhiteSpace(streamName) || streamName.Length > 128)
            throw new ArgumentException("Remote Scene stream name must contain between 1 and 128 characters.", nameof(streamName));

        return streamName.Trim();
    }
}
