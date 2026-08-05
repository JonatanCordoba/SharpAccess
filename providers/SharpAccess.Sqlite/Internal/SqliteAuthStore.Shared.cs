using SharpAccess.Domain;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using System.Globalization;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    private const string UserSelect = """
        SELECT id,email,normalized_email,password_hash,email_verified_utc,is_active,
               failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc
        FROM auth_users
        """;

    // Inserts one complete user record.
    private static Task<int> InsertUserAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        AuthUser user,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_users(
                id,email,normalized_email,password_hash,email_verified_utc,is_active,
                failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
            VALUES($id,$email,$normalized,$passwordHash,$verified,$active,$failed,$lockout,$version,$created,$updated);
            """,
            cancellationToken,
            ("$id", user.Id.ToString("D")),
            ("$email", user.Email),
            ("$normalized", user.NormalizedEmail),
            ("$passwordHash", user.PasswordHash),
            ("$verified", FormatNullable(user.EmailVerifiedUtc)),
            ("$active", user.IsActive ? 1 : 0),
            ("$failed", user.FailedLoginAttempts),
            ("$lockout", FormatNullable(user.LockoutEndUtc)),
            ("$version", user.SecurityVersion),
            ("$created", Format(user.CreatedUtc)),
            ("$updated", Format(user.UpdatedUtc)));

    // Invalidates access-token versions and refresh sessions for one user.
    private static async Task InvalidateUserSessionsInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_users SET security_version=security_version+1,updated_utc=$now WHERE id=$userId;",
            cancellationToken,
            ("$now", Format(now)),
            ("$userId", userId.ToString("D"))).ConfigureAwait(false);
        await RevokeUserTokensInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
    }

    // Invalidates every user assigned to one role in either explicit scope.
    private static async Task InvalidateRoleUsersInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_users
            SET security_version=security_version+1,updated_utc=$now
            WHERE id IN (
                SELECT user_id FROM auth_global_user_roles WHERE role_id=$roleId
                UNION
                SELECT user_id FROM auth_tenant_user_roles WHERE role_id=$roleId
            );
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$roleId", roleId.ToString("D"))).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=$now
            WHERE revoked_utc IS NULL
              AND user_id IN (
                  SELECT user_id FROM auth_global_user_roles WHERE role_id=$roleId
                  UNION
                  SELECT user_id FROM auth_tenant_user_roles WHERE role_id=$roleId
              );
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$roleId", roleId.ToString("D"))).ConfigureAwait(false);
    }

    // Revokes every active token in one family inside an optional transaction.
    private static Task<int> RevokeFamilyInternalAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_refresh_tokens SET revoked_utc=$now WHERE family_id=$familyId AND revoked_utc IS NULL;",
            cancellationToken,
            ("$now", Format(now)),
            ("$familyId", familyId.ToString("D")));

    // Finds a user by ID inside an existing transaction.
    private static async Task<AuthUser?> FindUserInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            UserSelect + " WHERE id=$userId LIMIT 1;",
            ("$userId", userId.ToString("D")));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Finds a user by normalized email inside an existing transaction.
    private static async Task<AuthUser?> FindUserByNormalizedEmailInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            UserSelect + " WHERE normalized_email=$normalized LIMIT 1;",
            ("$normalized", normalizedEmail));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Finds a user linked to an OAuth provider subject inside an existing transaction.
    private static async Task<AuthUser?> FindOAuthUserInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string provider,
        string subject,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT auth_users.id,auth_users.email,auth_users.normalized_email,auth_users.password_hash,
                   auth_users.email_verified_utc,auth_users.is_active,auth_users.failed_login_attempts,
                   auth_users.lockout_end_utc,auth_users.security_version,auth_users.created_utc,auth_users.updated_utc
            FROM auth_users
            INNER JOIN auth_oauth_accounts oa ON oa.user_id=auth_users.id
            WHERE oa.provider=$provider AND oa.provider_subject=$subject LIMIT 1;
            """,
            ("$provider", provider),
            ("$subject", subject));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Inserts a tenant membership only when both active user and tenant exist.
    private static Task<int> InsertTenantMembershipInternalAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO auth_tenant_memberships(tenant_id,user_id,created_utc)
            SELECT t.id,u.id,$created
            FROM auth_tenants t CROSS JOIN auth_users u
            WHERE t.id=$tenantId AND u.id=$userId AND u.is_active=1;
            """,
            cancellationToken,
            ("$created", Format(now)),
            ("$tenantId", tenantId.ToString("D")),
            ("$userId", userId.ToString("D")));

    // Inserts a role assignment only when the selected scoped role and membership exist.
    private static Task<bool> AssignRoleInternalAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        AssignRoleCoreAsync(connection, transaction, userId, roleId, tenantId, now, cancellationToken);

    // Executes one explicitly scoped role-assignment insert and reports whether a row was added.
    private static async Task<bool> AssignRoleCoreAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string sql = tenantId.HasValue
            ? """
              INSERT OR IGNORE INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
              SELECT $id,m.tenant_id,m.user_id,r.id,$created
              FROM auth_tenant_memberships m
              INNER JOIN auth_tenant_roles r ON r.tenant_id=m.tenant_id
              WHERE m.tenant_id=$tenantId
                AND m.user_id=$userId
                AND r.id=$roleId
                AND r.id<>$ownerRoleId;
              """
            : """
              INSERT OR IGNORE INTO auth_global_user_roles(id,user_id,role_id,created_utc)
              SELECT $id,u.id,r.id,$created
              FROM auth_users u CROSS JOIN auth_global_roles r
              WHERE u.id=$userId AND r.id=$roleId AND u.is_active=1;
              """;
        int affected = await ExecuteAsync(
            connection,
            transaction,
            sql,
            cancellationToken,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$created", Format(now)),
            ("$userId", userId.ToString("D")),
            ("$roleId", roleId.ToString("D")),
            ("$tenantId", tenantId?.ToString("D")),
            ("$ownerRoleId", TenantOwnerRoleId)).ConfigureAwait(false);
        return affected == 1;
    }

    // Reads one user from a prepared command.
    private static async Task<AuthUser?> ReadSingleUserAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapUser(reader) : null;
    }

    // Maps the standard user projection.
    private static AuthUser MapUser(DbDataReader reader) =>
        new(
            ParseGuid(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ReadNullableString(reader, 3),
            ReadNullableDate(reader, 4),
            reader.GetInt64(5) != 0,
            reader.GetInt32(6),
            ReadNullableDate(reader, 7),
            reader.GetInt32(8),
            ParseDate(reader.GetString(9)),
            ParseDate(reader.GetString(10)));

    // Maps a tenant projection.
    private static TenantRecord MapTenant(DbDataReader reader) =>
        new(ParseGuid(reader.GetString(0)), reader.GetString(1), reader.GetString(2), ParseDate(reader.GetString(3)));

    // Creates a parameterized SQLite command for an optional transaction.
    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        DbTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (SqliteTransaction?)transaction;
        foreach ((string name, object? value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return command;
    }

    // Executes one parameterized non-query asynchronously.
    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using SqliteCommand command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Adds a parameter without concatenating input into SQL.
    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        SqliteParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    // Rolls back best-effort without hiding the original exception.
    private static async Task SafeRollbackAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
        catch (SqliteException)
        {
        }
    }

    // Identifies SQLite constraint failures used for conflict-safe operations.
    private static bool IsConstraintViolation(SqliteException exception) => exception.SqliteErrorCode == 19;

    // Formats a UTC timestamp with lossless round-trip precision.
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // Formats a nullable UTC timestamp for ADO.NET parameters.
    private static string? FormatNullable(DateTimeOffset? value) => value.HasValue ? Format(value.Value) : null;

    // Parses a round-trip UTC timestamp.
    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    // Parses a canonical GUID.
    private static Guid ParseGuid(string value) => Guid.ParseExact(value, "D");

    // Reads a nullable string column.
    private static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // Reads a nullable timestamp column.
    private static DateTimeOffset? ReadNullableDate(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    // Reads a nullable GUID column.
    private static Guid? ReadNullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseGuid(reader.GetString(ordinal));
}
