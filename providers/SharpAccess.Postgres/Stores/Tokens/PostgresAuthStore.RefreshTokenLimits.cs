using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess.Domain;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Applies active-family and per-family token caps before persisting a new session.
    public async Task<bool> TryCreateRefreshTokenAsync(
        RefreshTokenRecord token,
        int maximumActiveFamiliesPerUser,
        int maximumActiveTokensPerFamily,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumActiveFamiliesPerUser, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumActiveTokensPerFamily, 1);

        await using NpgsqlConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken).ConfigureAwait(false);

        try
        {
            int familyTokens = await CountActiveRefreshTokensInFamilyForLimitAsync(
                connection,
                transaction,
                token.FamilyId,
                token.CreatedUtc,
                cancellationToken).ConfigureAwait(false);

            int activeFamilies = familyTokens == 0
                ? await CountActiveRefreshFamiliesForLimitAsync(
                    connection,
                    transaction,
                    token.UserId,
                    token.CreatedUtc,
                    cancellationToken).ConfigureAwait(false)
                : 0;

            if (familyTokens >= maximumActiveTokensPerFamily
                || (familyTokens == 0
                    && activeFamilies >= maximumActiveFamiliesPerUser))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await InsertRefreshTokenAsync(
                connection,
                transaction,
                token,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Revokes a replayed family and commits the detection audit in the same transaction.
    public async Task<bool> HandleRefreshTokenReplayAsync(
        string tokenHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken).ConfigureAwait(false);

        try
        {
            RefreshTokenRecord? existing = await FindRefreshTokenForLimitUpdateAsync(
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

    // Rotates a refresh token while enforcing the configured active-token cap.
    public Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default) =>
        RotateRefreshTokenAsync(existingTokenHash, replacement, now, maximumActiveTokensPerFamily, SecurityAuditEvidence.ForRefreshRotation(now, replacement.UserId, familyDetail: $"family={replacement.FamilyId:D}"), cancellationToken);

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

        await using NpgsqlConnection connection =
            await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken).ConfigureAwait(false);

        try
        {
            RefreshTokenRecord? existing = await FindRefreshTokenForLimitUpdateAsync(
                connection,
                transaction,
                existingTokenHash,
                cancellationToken).ConfigureAwait(false);

            if (existing is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TokenRotationResult(TokenRotationStatus.NotFound);
            }

            TokenRotationStatus? rejection =
                await GetRefreshRotationRejectionAsync(
                    connection,
                    transaction,
                    existing,
                    replacement,
                    now,
                    maximumActiveTokensPerFamily,
                    cancellationToken).ConfigureAwait(false);

            if (rejection.HasValue)
            {
                return await CompleteRejectedRefreshRotationAsync(
                    connection,
                    transaction,
                    existing,
                    now,
                    audit,
                    rejection.Value,
                    cancellationToken).ConfigureAwait(false);
            }

            await InsertRefreshTokenAsync(
                connection,
                transaction,
                replacement,
                cancellationToken).ConfigureAwait(false);

            int replaced = await MarkRefreshTokenReplacedAsync(
                connection,
                transaction,
                existing,
                replacement,
                now,
                cancellationToken).ConfigureAwait(false);

            if (replaced != 1)
            {
                return await CompleteRejectedRefreshRotationAsync(
                    connection,
                    transaction,
                    existing,
                    now,
                    audit,
                    TokenRotationStatus.Reused,
                    cancellationToken).ConfigureAwait(false);
            }

            return await CommitRefreshRotationOutcomeAsync(
                connection,
                transaction,
                existing,
                audit,
                TokenRotationStatus.Success,
                cancellationToken).ConfigureAwait(false);
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

    // Locks and reads the selected refresh token inside the limit-enforcement transaction.
    private static async Task<RefreshTokenRecord?> FindRefreshTokenForLimitUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            RefreshTokenSelect + " WHERE token_hash=@tokenHash LIMIT 1 FOR UPDATE;",
            ("@tokenHash", tokenHash));
        await using DbDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? MapRefreshToken(reader)
            : null;
    }

    // Counts a user's active refresh-token families inside the limit-enforcement transaction.
    private static async Task<int> CountActiveRefreshFamiliesForLimitAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT COUNT(DISTINCT family_id)
            FROM auth_refresh_tokens
            WHERE user_id=@userId
              AND revoked_utc IS NULL
              AND expires_utc>@now;
            """,
            ("@userId", userId),
            ("@now", ToUtc(now)));

        object? value =
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    // Counts one family's active refresh tokens inside the limit-enforcement transaction.
    private static async Task<int> CountActiveRefreshTokensInFamilyForLimitAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM auth_refresh_tokens
            WHERE family_id=@familyId
              AND revoked_utc IS NULL
              AND expires_utc>@now;
            """,
            ("@familyId", familyId),
            ("@now", ToUtc(now)));

        object? value =
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
