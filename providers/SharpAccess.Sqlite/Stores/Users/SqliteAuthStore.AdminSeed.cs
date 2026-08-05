using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Creates provider-contract audit evidence for direct seeding calls.
    public Task SeedAdminAsync(
        AdminSeedOptions options,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        SeedAdminAsync(
            options,
            passwordHash,
            now,
            SecurityAuditEvidence.ForStoreMutation(now, "administrator_seeded"),
            cancellationToken);

    // Seeds or rotates a verified local administrator and revokes all prior sessions.
    public async Task SeedAdminAsync(
        AdminSeedOptions options,
        string passwordHash,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        string normalizedEmail = AuthService.NormalizeEmail(options.Email);
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO auth_users(
                    id,email,normalized_email,password_hash,email_verified_utc,is_active,
                    failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
                VALUES($id,$email,$normalized,$hash,$now,1,0,NULL,1,$now,$now)
                ON CONFLICT(normalized_email) DO UPDATE SET
                    email=excluded.email,
                    password_hash=excluded.password_hash,
                    email_verified_utc=excluded.email_verified_utc,
                    is_active=1,
                    failed_login_attempts=0,
                    lockout_end_utc=NULL,
                    security_version=auth_users.security_version+1,
                    updated_utc=excluded.updated_utc;
                """,
                cancellationToken,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$email", options.Email.Trim()),
                ("$normalized", normalizedEmail),
                ("$hash", passwordHash),
                ("$now", Format(now))).ConfigureAwait(false);

            AuthUser user = (await FindUserByNormalizedEmailInternalAsync(
                connection,
                transaction,
                normalizedEmail,
                cancellationToken).ConfigureAwait(false))!;
            await RevokeUserTokensInternalAsync(connection, transaction, user.Id, now, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT OR IGNORE INTO auth_global_user_roles(id,user_id,role_id,created_utc)
                SELECT $id,u.id,r.id,$created
                FROM auth_users u CROSS JOIN auth_global_roles r
                WHERE u.id=$userId AND u.is_active=1 AND r.id=$roleId;
                """,
                cancellationToken,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$userId", user.Id.ToString("D")),
                ("$roleId", AdminRoleId),
                ("$created", Format(now))).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit with { UserId = user.Id }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }
}
