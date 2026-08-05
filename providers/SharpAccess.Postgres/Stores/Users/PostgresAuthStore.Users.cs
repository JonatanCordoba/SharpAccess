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
    // Creates a user and initial verification token in one transaction.
    public async Task<bool> CreateUserWithVerificationTokenAsync(
        AuthUser user,
        string verificationTokenHash,
        DateTimeOffset verificationExpiresUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertUserAsync(connection, transaction, user, cancellationToken).ConfigureAwait(false);
            await InsertOneTimeTokenAsync(
                connection,
                transaction,
                user.Id,
                VerificationPurpose,
                verificationTokenHash,
                user.CreatedUtc,
                verificationExpiresUtc,
                cancellationToken).ConfigureAwait(false);
            await AssignRoleInternalAsync(
                connection,
                transaction,
                user.Id,
                Guid.Parse(UserRoleId),
                null,
                user.CreatedUtc,
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
    // Finds a user by normalized email.
    public async Task<AuthUser?> FindUserByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            UserSelect + " WHERE normalized_email=@normalizedEmail LIMIT 1;",
            ("@normalizedEmail", normalizedEmail));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }
    // Finds a user by identifier.
    public async Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            UserSelect + " WHERE id=@userId LIMIT 1;",
            ("@userId", userId));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }
    // Lists one bounded keyset page of users in stable reverse-creation order.
    public async Task<AuthPageSlice<AuthUser>> ListUsersAsync(
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(AuthUser Item, AuthPageBoundary Boundary)> users = [];
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = page.After is null
            ? CreateCommand(connection, null,
                UserSelect + " ORDER BY created_utc DESC,id ASC LIMIT @fetchLimit;",
                ("@fetchLimit", fetchLimit))
            : CreateCommand(connection, null,
                UserSelect + " WHERE created_utc < @afterCreated OR (created_utc = @afterCreated AND id > @afterId) ORDER BY created_utc DESC,id ASC LIMIT @fetchLimit;",
                ("@afterCreated", ToUtc(page.After.CreatedUtc)),
                ("@afterId", page.After.Id),
                ("@fetchLimit", fetchLimit));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            AuthUser user = MapUser(reader);
            users.Add((user, new AuthPageBoundary(user.CreatedUtc, user.Id)));
        }

        return AuthPageSupport.CreateSlice(users, pageLimit);
    }
    // Atomically increments failed-login state and applies the configured lockout threshold.
    public async Task RecordLoginFailureAsync(
        Guid userId,
        int failureThreshold,
        DateTimeOffset lockoutEndUtc,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_users
            SET failed_login_attempts = CASE
                    WHEN lockout_end_utc IS NOT NULL AND lockout_end_utc <= @updated THEN 1
                    ELSE LEAST(failed_login_attempts + 1, @threshold)
                END,
                lockout_end_utc = CASE
                    WHEN (CASE
                        WHEN lockout_end_utc IS NOT NULL AND lockout_end_utc <= @updated THEN 1
                        ELSE failed_login_attempts + 1
                    END) >= @threshold THEN @lockout
                    ELSE NULL
                END,
                updated_utc = @updated
            WHERE id = @userId
              AND is_active = true
              AND (lockout_end_utc IS NULL OR lockout_end_utc <= @updated);
            """,
            cancellationToken,
            ("@threshold", failureThreshold),
            ("@lockout", ToUtc(lockoutEndUtc)),
            ("@updated", ToUtc(updatedUtc)),
            ("@userId", userId)).ConfigureAwait(false);
    }
    // Clears failed-login state after successful authentication.
    public async Task ResetLoginFailuresAsync(
        Guid userId,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            "UPDATE auth_users SET failed_login_attempts=0,lockout_end_utc=NULL,updated_utc=@updated WHERE id=@userId;",
            cancellationToken,
            ("@updated", ToUtc(updatedUtc)),
            ("@userId", userId)).ConfigureAwait(false);
    }
    // Replaces a password hash during parameter migration without invalidating current sessions.
    public async Task<bool> UpdatePasswordHashAsync(
        Guid userId,
        string expectedPasswordHash,
        int expectedSecurityVersion,
        string passwordHash,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        int affected = await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_users SET password_hash=@hash,updated_utc=@updated
            WHERE id=@userId AND is_active=true
              AND password_hash=@expectedHash AND security_version=@expectedVersion;
            """,
            cancellationToken,
            ("@hash", passwordHash),
            ("@expectedHash", expectedPasswordHash),
            ("@expectedVersion", expectedSecurityVersion),
            ("@updated", ToUtc(updatedUtc)),
            ("@userId", userId)).ConfigureAwait(false);
        return affected == 1;
    }
    // Changes a password and revokes all refresh sessions in one transaction.
    public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ChangePasswordAsync(userId, passwordHash, now, SecurityAuditEvidence.ForStoreMutation(now, "password_changed", userId), cancellationToken);

    // Changes a password, revokes sessions, and writes its audit evidence in one transaction.
    public async Task<bool> ChangePasswordAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
                ("@userId", userId)).ConfigureAwait(false);
            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await RevokeUserTokensInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
    // Changes account active state, increments security version, and revokes every refresh session.
    public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        SetUserActiveAsync(userId, isActive, now, SecurityAuditEvidence.ForStoreMutation(now, isActive ? "user_activated" : "user_revoked", userId), cancellationToken);

    // Changes account state, revokes sessions, and writes its audit evidence in one transaction.
    public async Task<bool> SetUserActiveAsync(
        Guid userId,
        bool isActive,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int updated = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE auth_users
                SET is_active=@active,security_version=security_version+1,
                    failed_login_attempts=0,lockout_end_utc=NULL,updated_utc=@now
                WHERE id=@userId;
                """,
                cancellationToken,
                ("@active", isActive),
                ("@now", ToUtc(now)),
                ("@userId", userId)).ConfigureAwait(false);
            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await RevokeUserTokensInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
}
