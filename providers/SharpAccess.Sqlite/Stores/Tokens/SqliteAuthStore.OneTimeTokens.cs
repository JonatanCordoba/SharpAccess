using SharpAccess.Domain;
using Microsoft.Data.Sqlite;
using System.Data.Common;

using System.Data;
namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
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
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"UPDATE {OneTimeTokenTable(purpose)} SET consumed_utc=$now WHERE user_id=$userId AND purpose=$purpose AND consumed_utc IS NULL;",
                cancellationToken,
                ("$now", Format(createdUtc)),
                ("$userId", userId.ToString("D")),
                ("$purpose", purpose)).ConfigureAwait(false);
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
        catch (SqliteException exception) when (IsConstraintViolation(exception))
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
    public Task<Guid?> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        VerifyEmailAsync(
            tokenHash,
            now,
            SecurityAuditEvidence.ForStoreMutation(now, "email_verified"),
            cancellationToken);

    // Consumes a verification token, updates its user, and writes enriched audit evidence atomically.
    public async Task<Guid?> VerifyEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                "UPDATE auth_users SET email_verified_utc=COALESCE(email_verified_utc,$now),updated_utc=$now WHERE id=$userId AND is_active=1;",
                cancellationToken,
                ("$now", Format(now)),
                ("$userId", token.UserId.ToString("D"))).ConfigureAwait(false);
            if (consumed != 1 || updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertAuditAsync(
                connection,
                transaction,
                audit with { UserId = token.UserId },
                cancellationToken).ConfigureAwait(false);
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
    public Task<Guid?> ResetPasswordAsync(
        string tokenHash,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        ResetPasswordAsync(
            tokenHash,
            passwordHash,
            now,
            SecurityAuditEvidence.ForStoreMutation(now, "password_reset_completed"),
            cancellationToken);

    // Consumes a reset token, updates its user, revokes sessions, and writes enriched audit evidence atomically.
    public async Task<Guid?> ResetPasswordAsync(
        string tokenHash,
        string passwordHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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
                SET password_hash=$hash,security_version=security_version+1,
                    failed_login_attempts=0,lockout_end_utc=NULL,updated_utc=$now
                WHERE id=$userId AND is_active=1;
                """,
                cancellationToken,
                ("$hash", passwordHash),
                ("$now", Format(now)),
                ("$userId", token.UserId.ToString("D"))).ConfigureAwait(false);
            await RevokeUserTokensInternalAsync(connection, transaction, token.UserId, now, cancellationToken).ConfigureAwait(false);
            if (consumed != 1 || updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertAuditAsync(
                connection,
                transaction,
                audit with { UserId = token.UserId },
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return token.UserId;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Inserts a one-time token hash and metadata.
    private static Task<int> InsertOneTimeTokenAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            $"""
            INSERT INTO {OneTimeTokenTable(purpose)}(
                id,user_id,purpose,token_hash,hash_key_version,created_utc,expires_utc,consumed_utc)
            VALUES($id,$userId,$purpose,$tokenHash,substr($tokenHash,1,8),$created,$expires,NULL);
            """,
            cancellationToken,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$userId", userId.ToString("D")),
            ("$purpose", purpose),
            ("$tokenHash", tokenHash),
            ("$created", Format(createdUtc)),
            ("$expires", Format(expiresUtc)));

    // Finds one active one-time token inside an existing transaction.
    private static async Task<OneTimeTokenRecord?> FindActiveOneTimeTokenAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string purpose,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            $"""
            SELECT user_id,purpose,expires_utc
            FROM {OneTimeTokenTable(purpose)}
            WHERE purpose=$purpose AND token_hash=$tokenHash
              AND consumed_utc IS NULL AND expires_utc>$now
            LIMIT 1;
            """,
            ("$purpose", purpose),
            ("$tokenHash", tokenHash),
            ("$now", Format(now)));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new OneTimeTokenRecord(ParseGuid(reader.GetString(0)), reader.GetString(1), ParseDate(reader.GetString(2)))
            : null;
    }

    // Marks one active one-time token consumed exactly once.
    private static Task<int> ConsumeTokenInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string purpose,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            $"""
            UPDATE {OneTimeTokenTable(purpose)}
            SET consumed_utc=$now
            WHERE purpose=$purpose AND token_hash=$tokenHash
              AND consumed_utc IS NULL AND expires_utc>$now;
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$purpose", purpose),
            ("$tokenHash", tokenHash));

    // Maps a trusted internal token purpose to its dedicated provider-owned table.
    private static string OneTimeTokenTable(string purpose) => purpose switch
    {
        VerificationPurpose => "auth_email_verification_tokens",
        PasswordResetPurpose => "auth_password_reset_tokens",
        _ when purpose.StartsWith("oauth_exchange:", StringComparison.Ordinal) => "auth_oauth_exchange_codes",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), "Unsupported one-time token purpose.")
    };



    // Creates a general-purpose one-time token such as an OAuth exchange code.
    public async Task<bool> CreateOneTimeTokenAsync(
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset createdUtc,
        DateTimeOffset expiresUtc,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        catch (SqliteException exception) when (IsConstraintViolation(exception))
        {
            return false;
        }
    }

    // Atomically consumes one general-purpose one-time token.
    public async Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(
        string purpose,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            OneTimeTokenRecord? token = await FindActiveOneTimeTokenAsync(
                connection,
                transaction,
                purpose,
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
                purpose,
                tokenHash,
                now,
                cancellationToken).ConfigureAwait(false);
            if (consumed != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return token;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

}
