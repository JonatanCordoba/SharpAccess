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
    // Invalidates previous one-time tokens for a purpose and inserts a replacement transactionally.
    public async Task<bool> ReplaceOneTimeTokenAsync(
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (NpgsqlCommand lockUser = CreateCommand(
                connection,
                transaction,
                "SELECT 1 FROM auth_users WHERE id=@userId FOR UPDATE;",
                ("@userId", userId)))
            {
                object? locked = await lockUser.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (locked is null)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return false;
                }
            }

            await ExecuteAsync(
                connection,
                transaction,
                $"UPDATE {OneTimeTokenTable(purpose)} SET consumed_utc=@now WHERE user_id=@userId AND purpose=@purpose AND consumed_utc IS NULL;",
                cancellationToken,
                ("@now", ToUtc(createdUtc)),
                ("@userId", userId),
                ("@purpose", purpose)).ConfigureAwait(false);
            await InsertOneTimeTokenAsync(
                connection,
                transaction,
                userId,
                purpose,
                tokenHash,
                createdUtc,
                expiresUtc,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException exception) when (IsConstraintViolation(exception))
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            return false;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
    // Consumes an email-verification token and marks the account verified atomically.
    public Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        VerifyEmailAsync(tokenHash, now, SecurityAuditEvidence.ForStoreMutation(now, "email_verified"), cancellationToken);
    // Consumes a verification token and commits enriched audit evidence with the user update.
    public async Task<Guid?> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OneTimeTokenRecord? token = await FindActiveOneTimeTokenAsync(
                connection,
                transaction,
                VerificationPurpose,
                tokenHash,
                now,
                cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            int consumed = await ConsumeTokenInternalAsync(
                connection,
                transaction,
                VerificationPurpose,
                tokenHash,
                now,
                cancellationToken).ConfigureAwait(false);
            int updated = await ExecuteAsync(
                connection,
                transaction,
                "UPDATE auth_users SET email_verified_utc=COALESCE(email_verified_utc,@now),updated_utc=@now WHERE id=@userId AND is_active=true;",
                cancellationToken,
                ("@now", ToUtc(now)),
                ("@userId", token.UserId)).ConfigureAwait(false);
            if (consumed != 1 || updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertAuditAsync(connection, transaction, audit with { UserId = token.UserId }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return token.UserId;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
    // Consumes a reset token, changes the password, increments security version, and revokes sessions atomically.
    public Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ResetPasswordAsync(tokenHash, passwordHash, now, SecurityAuditEvidence.ForStoreMutation(now, "password_reset_completed"), cancellationToken);
    // Consumes a reset token and commits enriched audit evidence with password and session changes.
    public async Task<Guid?> ResetPasswordAsync(
        string tokenHash,
        string passwordHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            OneTimeTokenRecord? token = await FindActiveOneTimeTokenAsync(
                connection,
                transaction,
                PasswordResetPurpose,
                tokenHash,
                now,
                cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            int consumed = await ConsumeTokenInternalAsync(
                connection,
                transaction,
                PasswordResetPurpose,
                tokenHash,
                now,
                cancellationToken).ConfigureAwait(false);
            int updated = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE auth_users
                SET password_hash=@hash,security_version=security_version+1,
                    failed_login_attempts=0,lockout_end_utc=NULL,updated_utc=@now
                WHERE id=@userId AND is_active=true;
                """,
                cancellationToken,
                ("@hash", passwordHash),
                ("@now", ToUtc(now)),
                ("@userId", token.UserId)).ConfigureAwait(false);
            await RevokeUserTokensInternalAsync(connection, transaction, token.UserId, now, cancellationToken).ConfigureAwait(false);
            if (consumed != 1 || updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertAuditAsync(connection, transaction, audit with { UserId = token.UserId }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return token.UserId;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
    // Creates a general-purpose one-time token such as an OAuth exchange code.
    public async Task<bool> CreateOneTimeTokenAsync(
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertOneTimeTokenAsync(
                connection,
                null,
                userId,
                purpose,
                tokenHash,
                createdUtc,
                expiresUtc,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (PostgresException exception) when (IsConstraintViolation(exception))
        {
            return false;
        }
    }
    // Atomically consumes one general-purpose one-time token with a single winner-producing statement.
    public async Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(
        string purpose,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            $"""
            UPDATE {OneTimeTokenTable(purpose)}
            SET consumed_utc=@now
            WHERE purpose=@purpose AND token_hash=@tokenHash
              AND consumed_utc IS NULL AND expires_utc>@now
            RETURNING user_id,purpose,expires_utc;
            """,
            ("@now", ToUtc(now)),
            ("@purpose", purpose),
            ("@tokenHash", tokenHash));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new OneTimeTokenRecord(reader.GetGuid(0), reader.GetString(1), ReadDate(reader, 2))
            : null;
    }
}
