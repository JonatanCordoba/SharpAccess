using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Services;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Persists a hashed refresh token.
    public async Task CreateRefreshTokenAsync(
        RefreshTokenRecord token,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertRefreshTokenAsync(connection, null, token, cancellationToken).ConfigureAwait(false);
    }
    // Finds a refresh token by keyed one-way hash.
    public async Task<RefreshTokenRecord?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            RefreshTokenSelect + " WHERE token_hash=@tokenHash LIMIT 1;",
            ("@tokenHash", tokenHash));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapRefreshToken(reader) : null;
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
            SecurityAuditEvidence.ForRefreshRotation(
                now,
                replacement.UserId,
                familyDetail: $"family={replacement.FamilyId:D}"),
            cancellationToken);
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
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                    "UPDATE auth_refresh_tokens SET revoked_utc=@now WHERE id=@id AND revoked_utc IS NULL;",
                    cancellationToken,
                    ("@now", ToUtc(now)),
                    ("@id", token.Id)).ConfigureAwait(false);
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
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
}
