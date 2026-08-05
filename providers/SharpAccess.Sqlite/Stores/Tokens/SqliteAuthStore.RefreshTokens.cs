using SharpAccess.Domain;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Persists a hashed refresh token.
    public async Task CreateRefreshTokenAsync(
        RefreshTokenRecord token,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertRefreshTokenAsync(connection, null, token, cancellationToken).ConfigureAwait(false);
    }

    // Applies active-family and per-family token caps before persisting a new session.
    public async Task<bool> TryCreateRefreshTokenAsync(
        RefreshTokenRecord token,
        int maximumActiveFamiliesPerUser,
        int maximumActiveTokensPerFamily,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumActiveFamiliesPerUser, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumActiveTokensPerFamily, 1);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
        try
        {
            int familyTokens = await CountActiveFamilyTokensAsync(
                connection,
                transaction,
                token.FamilyId,
                token.CreatedUtc,
                cancellationToken).ConfigureAwait(false);
            int activeFamilies = familyTokens == 0
                ? await CountActiveFamiliesAsync(
                    connection,
                    transaction,
                    token.UserId,
                    token.CreatedUtc,
                    cancellationToken).ConfigureAwait(false)
                : 0;
            if (familyTokens >= maximumActiveTokensPerFamily
                || (familyTokens == 0 && activeFamilies >= maximumActiveFamiliesPerUser))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await InsertRefreshTokenAsync(connection, transaction, token, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Finds a refresh token by keyed one-way hash.
    public async Task<RefreshTokenRecord?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = RefreshTokenSelect + " WHERE token_hash=$tokenHash LIMIT 1;";
        AddParameter(command, "$tokenHash", tokenHash);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapRefreshToken(reader) : null;
    }

    // Revokes a replayed family and commits the detection audit in the same transaction.
    public async Task<bool> HandleRefreshTokenReplayAsync(
        string tokenHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            RefreshTokenRecord? existing = await FindRefreshTokenInternalAsync(
                connection,
                transaction,
                tokenHash,
                cancellationToken).ConfigureAwait(false);
            if (existing is null || !existing.RevokedUtc.HasValue)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await RevokeFamilyInternalAsync(connection, transaction, existing.FamilyId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit with { UserId = existing.UserId }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Rotates a refresh token atomically and revokes its family on reuse or invalid account state.
    public Task<TokenRotationResult> RotateRefreshTokenAsync(
        string existingTokenHash,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        RotateRefreshTokenAsync(
            existingTokenHash,
            replacement,
            now,
            int.MaxValue,
            SecurityAuditEvidence.ForRefreshRotation(now, replacement.UserId, familyDetail: $"family={replacement.FamilyId:D}"),
            cancellationToken);

    // Rotates with bounded provider-contract audit evidence.
    public Task<TokenRotationResult> RotateRefreshTokenAsync(
        string existingTokenHash,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        int maximumActiveTokensPerFamily,
        CancellationToken cancellationToken = default) =>
        RotateRefreshTokenAsync(
            existingTokenHash,
            replacement,
            now,
            maximumActiveTokensPerFamily,
            SecurityAuditEvidence.ForRefreshRotation(now, replacement.UserId, familyDetail: $"family={replacement.FamilyId:D}"),
            cancellationToken);

    // Rotates a token and commits exactly one outcome-specific audit row with every state change.
    public async Task<TokenRotationResult> RotateRefreshTokenAsync(
        string existingTokenHash,
        RefreshTokenRecord replacement,
        DateTimeOffset now,
        int maximumActiveTokensPerFamily,
        RefreshTokenAuditEvidence audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumActiveTokensPerFamily, 1);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
        try
        {
            return await RotateRefreshTokenCoreAsync(
                connection,
                transaction,
                existingTokenHash,
                replacement,
                now,
                maximumActiveTokensPerFamily,
                audit,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Revokes one selected token or its family after enforcing ownership.
    public Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeRefreshTokenAsync(tokenHash, requestingUserId, allowAnyUser, revokeFamily, now, SecurityAuditEvidence.ForStoreMutation(now, revokeFamily ? "refresh_token_family_revoked" : "refresh_token_revoked", requestingUserId), cancellationToken);

    // Revokes a selected token or family and commits audit evidence only when state changed.
    public async Task<bool> RevokeRefreshTokenAsync(
        string tokenHash,
        Guid requestingUserId,
        bool allowAnyUser,
        bool revokeFamily,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RefreshTokenRecord? token = await FindRefreshTokenInternalAsync(
                connection,
                transaction,
                tokenHash,
                cancellationToken).ConfigureAwait(false);
            if (token is null || (!allowAnyUser && token.UserId != requestingUserId))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            int affected = revokeFamily
                ? await RevokeFamilyInternalAsync(connection, transaction, token.FamilyId, now, cancellationToken).ConfigureAwait(false)
                : await ExecuteAsync(
                    connection,
                    transaction,
                    "UPDATE auth_refresh_tokens SET revoked_utc=$now WHERE id=$id AND revoked_utc IS NULL;",
                    cancellationToken,
                    ("$now", Format(now)),
                    ("$id", token.Id.ToString("D"))).ConfigureAwait(false);
            if (affected > 0)
            {
                await InsertAuditAsync(connection, transaction, audit with { UserId = token.UserId }, cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected > 0 || token.RevokedUtc.HasValue;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Revokes every active token in one family.
    public Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeRefreshTokenFamilyAsync(familyId, now, SecurityAuditEvidence.ForStoreMutation(now, "refresh_token_family_revoked"), cancellationToken);

    // Revokes a family and commits audit evidence only when state changed.
    public async Task<int> RevokeRefreshTokenFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int affected = await RevokeFamilyInternalAsync(connection, transaction, familyId, now, cancellationToken).ConfigureAwait(false);
            if (affected > 0)
            {
                await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Revokes every active refresh token for a user.
    public Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeAllUserRefreshTokensAsync(userId, now, SecurityAuditEvidence.ForStoreMutation(now, "user_refresh_tokens_revoked", userId), cancellationToken);

    // Revokes every user token and commits audit evidence only when state changed.
    public async Task<int> RevokeAllUserRefreshTokensAsync(
        Guid userId,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int affected = await RevokeUserTokensInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
            if (affected > 0)
            {
                await InsertAuditAsync(connection, transaction, audit with { UserId = userId }, cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Enriches an outcome template with provider-trusted token ownership.
    private static AuditRecord EnrichRotationAudit(AuditRecord audit, RefreshTokenRecord existing) =>
        audit with { UserId = existing.UserId };

    private const string RefreshTokenSelect = """
        SELECT id,user_id,token_hash,family_id,security_version,ip_address,user_agent,
               authenticated_utc,created_utc,expires_utc,revoked_utc,replaced_by_token_id
        FROM auth_refresh_tokens
        """;

    // Inserts a hashed refresh token and request metadata.
    private static Task<int> InsertRefreshTokenAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        RefreshTokenRecord token,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_refresh_tokens(
                id,user_id,token_hash,hash_key_version,family_id,security_version,ip_address,user_agent,
                authenticated_utc,created_utc,expires_utc,revoked_utc,replaced_by_token_id)
            VALUES(
                $id,$userId,$tokenHash,substr($tokenHash,1,8),$familyId,$version,$ip,$agent,
                $authenticated,$created,$expires,$revoked,$replacement);
            """,
            cancellationToken,
            ("$id", token.Id.ToString("D")),
            ("$userId", token.UserId.ToString("D")),
            ("$tokenHash", token.TokenHash),
            ("$familyId", token.FamilyId.ToString("D")),
            ("$version", token.SecurityVersion),
            ("$ip", token.IpAddress),
            ("$agent", token.UserAgent),
            ("$authenticated", Format(token.AuthenticatedUtc)),
            ("$created", Format(token.CreatedUtc)),
            ("$expires", Format(token.ExpiresUtc)),
            ("$revoked", FormatNullable(token.RevokedUtc)),
            ("$replacement", token.ReplacedByTokenId?.ToString("D")));

    // Finds a refresh token inside an existing transaction.
    private static async Task<RefreshTokenRecord?> FindRefreshTokenInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            RefreshTokenSelect + " WHERE token_hash=$tokenHash LIMIT 1;",
            ("$tokenHash", tokenHash));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapRefreshToken(reader) : null;
    }

    // Revokes every active refresh token for one user inside an optional transaction.
    private static Task<int> RevokeUserTokensInternalAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_refresh_tokens SET revoked_utc=$now WHERE user_id=$userId AND revoked_utc IS NULL;",
            cancellationToken,
            ("$now", Format(now)),
            ("$userId", userId.ToString("D")));

    // Counts a user's active refresh-token families inside the limit-enforcement transaction.
    private static async Task<int> CountActiveFamiliesAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT COUNT(DISTINCT family_id)
            FROM auth_refresh_tokens
            WHERE user_id=$userId AND revoked_utc IS NULL AND expires_utc>$now;
            """,
            ("$userId", userId.ToString("D")),
            ("$now", Format(now)));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    // Counts one family's active refresh tokens inside the limit-enforcement transaction.
    private static async Task<int> CountActiveFamilyTokensAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM auth_refresh_tokens
            WHERE family_id=$familyId AND revoked_utc IS NULL AND expires_utc>$now;
            """,
            ("$familyId", familyId.ToString("D")),
            ("$now", Format(now)));
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    // Maps the standard refresh-token projection.
    private static RefreshTokenRecord MapRefreshToken(DbDataReader reader) =>
        new(
            ParseGuid(reader.GetString(0)),
            ParseGuid(reader.GetString(1)),
            reader.GetString(2),
            ParseGuid(reader.GetString(3)),
            reader.GetInt32(4),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6),
            ParseDate(reader.GetString(7)),
            ParseDate(reader.GetString(8)),
            ParseDate(reader.GetString(9)),
            ReadNullableDate(reader, 10),
            ReadNullableGuid(reader, 11));


}
