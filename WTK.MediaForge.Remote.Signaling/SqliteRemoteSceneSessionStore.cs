using Microsoft.Data.Sqlite;

namespace WTK.MediaForge.Remote.Signaling;

public sealed class SqliteRemoteSceneSessionStore : IRemoteSceneSessionStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;
    private int _disposed;

    public SqliteRemoteSceneSessionStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A SQLite database path is required.", nameof(databasePath));

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task CreateAsync(RemoteSceneStoredSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO remote_scene_sessions (
                session_id, stream_name, owner_role, invitation_hash, owner_token_hash,
                participant_token_hash, created_unix_ms, expires_unix_ms, redeemed_unix_ms,
                tenant_id, user_id, revoked_unix_ms)
            VALUES ($session_id, $stream_name, $owner_role, $invitation_hash, $owner_token_hash,
                NULL, $created, $expires, NULL, $tenant_id, $user_id, NULL);
            """;
        command.Parameters.AddWithValue("$session_id", session.SessionId.ToString("N"));
        command.Parameters.AddWithValue("$stream_name", session.StreamName);
        command.Parameters.AddWithValue("$owner_role", (int)session.OwnerRole);
        command.Parameters.Add("$invitation_hash", SqliteType.Blob).Value = session.InvitationCodeHash;
        command.Parameters.Add("$owner_token_hash", SqliteType.Blob).Value = session.OwnerTokenHash;
        command.Parameters.AddWithValue("$created", session.CreatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$expires", session.ExpiresAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$tenant_id", session.TenantId);
        command.Parameters.AddWithValue("$user_id", session.UserId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteSceneInvitationRedemption?> RedeemAsync(
        byte[] invitationCodeHash,
        byte[] participantTokenHash,
        DateTimeOffset redeemedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitationCodeHash);
        ArgumentNullException.ThrowIfNull(participantTokenHash);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT session_id, stream_name, owner_role, expires_unix_ms, tenant_id, user_id
            FROM remote_scene_sessions
            WHERE invitation_hash = $hash
              AND redeemed_unix_ms IS NULL
              AND revoked_unix_ms IS NULL
              AND expires_unix_ms > $now;
            """;
        select.Parameters.Add("$hash", SqliteType.Blob).Value = invitationCodeHash;
        select.Parameters.AddWithValue("$now", redeemedAt.ToUnixTimeMilliseconds());

        Guid sessionId;
        string streamName;
        RemoteScenePeerRole ownerRole;
        DateTimeOffset expiresAt;
        string tenantId;
        string userId;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            sessionId = Guid.ParseExact(reader.GetString(0), "N");
            streamName = reader.GetString(1);
            ownerRole = (RemoteScenePeerRole)reader.GetInt32(2);
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
            tenantId = reader.GetString(4);
            userId = reader.GetString(5);
        }

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE remote_scene_sessions
            SET participant_token_hash = $participant_hash,
                redeemed_unix_ms = $redeemed
            WHERE session_id = $session_id
              AND redeemed_unix_ms IS NULL
              AND revoked_unix_ms IS NULL
              AND expires_unix_ms > $redeemed;
            """;
        update.Parameters.Add("$participant_hash", SqliteType.Blob).Value = participantTokenHash;
        update.Parameters.AddWithValue("$redeemed", redeemedAt.ToUnixTimeMilliseconds());
        update.Parameters.AddWithValue("$session_id", sessionId.ToString("N"));
        if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        var participantRole = ownerRole == RemoteScenePeerRole.Publisher
            ? RemoteScenePeerRole.Subscriber
            : RemoteScenePeerRole.Publisher;
        return new RemoteSceneInvitationRedemption(sessionId, streamName, participantRole, expiresAt, tenantId, userId);
    }

    public async Task<RemoteSceneSessionAccess?> AuthorizeAsync(
        Guid sessionId,
        byte[] accessTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessTokenHash);
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT stream_name, owner_role, owner_token_hash, participant_token_hash, expires_unix_ms, tenant_id, user_id
            FROM remote_scene_sessions
            WHERE session_id = $session_id AND expires_unix_ms > $now AND revoked_unix_ms IS NULL;
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("N"));
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var streamName = reader.GetString(0);
        var ownerRole = (RemoteScenePeerRole)reader.GetInt32(1);
        var ownerHash = (byte[])reader[2];
        var participantHash = reader.IsDBNull(3) ? null : (byte[])reader[3];
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4));
        var tenantId = reader.GetString(5);
        var userId = reader.GetString(6);

        if (System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ownerHash, accessTokenHash))
            return new RemoteSceneSessionAccess(sessionId, streamName, ownerRole, expiresAt, tenantId, userId);

        if (participantHash is not null &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(participantHash, accessTokenHash))
        {
            var participantRole = ownerRole == RemoteScenePeerRole.Publisher
                ? RemoteScenePeerRole.Subscriber
                : RemoteScenePeerRole.Publisher;
            return new RemoteSceneSessionAccess(sessionId, streamName, participantRole, expiresAt, tenantId, userId);
        }

        return null;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM remote_scene_sessions WHERE expires_unix_ms <= $now;";
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE remote_scene_sessions SET revoked_unix_ms = $now WHERE session_id = $session_id;";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _initializeGate.Dispose();
            using var poolKey = new SqliteConnection(_connectionString);
            SqliteConnection.ClearPool(poolKey);
        }
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized))
            return;

        await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = FULL;
                CREATE TABLE IF NOT EXISTS remote_scene_sessions (
                    session_id TEXT PRIMARY KEY,
                    stream_name TEXT NOT NULL,
                    owner_role INTEGER NOT NULL,
                    invitation_hash BLOB NOT NULL UNIQUE,
                    owner_token_hash BLOB NOT NULL,
                    participant_token_hash BLOB NULL,
                    created_unix_ms INTEGER NOT NULL,
                    expires_unix_ms INTEGER NOT NULL,
                    redeemed_unix_ms INTEGER NULL
                    ,tenant_id TEXT NOT NULL DEFAULT 'default'
                    ,user_id TEXT NOT NULL DEFAULT 'operator'
                    ,revoked_unix_ms INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS ix_remote_scene_sessions_expiry
                    ON remote_scene_sessions (expires_unix_ms);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
