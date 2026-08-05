using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Services;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore(
    IPostgresAuthConnectionFactory connections,
    IAuthMigrationProvider migrations) : IAuthDatabase
{
    private static readonly SemaphoreSlim MigrationLock = new(1, 1);
    private readonly IPostgresAuthConnectionFactory _connections = connections;
    private readonly IAuthMigrationProvider _migrations = migrations;
    private const string VerificationPurpose = "email_verification";
    private const string PasswordResetPurpose = "password_reset";
    private const string AdminRoleId = "10000000-0000-0000-0000-000000000001";
    private const string UserRoleId = "10000000-0000-0000-0000-000000000002";
    private const string UserSelect = """
        SELECT id,email,normalized_email,password_hash,email_verified_utc,is_active,
               failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc
        FROM auth_users
        """;
    private const string RefreshTokenSelect = """
        SELECT id,user_id,token_hash,family_id,security_version,ip_address,user_agent,
               authenticated_utc,created_utc,expires_utc,revoked_utc,replaced_by_token_id
        FROM auth_refresh_tokens
        """;


    // Serializes provider migration discovery and execution within the process.
    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            null,
            """
            CREATE TABLE IF NOT EXISTS auth_schema_migrations(
                id text PRIMARY KEY,
                applied_utc timestamptz NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        HashSet<string> applied = await ReadAppliedMigrationIdsAsync(connection, cancellationToken).ConfigureAwait(false);
        foreach (AuthMigration migration in _migrations.GetMigrations())
        {
            if (applied.Contains(migration.Id))
            {
                continue;
            }

            await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
        }
    }

    // Reads all applied migration identifiers from the migration ledger.
    private static async Task<HashSet<string>> ReadAppliedMigrationIdsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        HashSet<string> applied = new(StringComparer.Ordinal);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            "SELECT id FROM auth_schema_migrations;");
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    // Claims and applies one migration inside one serializable transaction.
    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        AuthMigration migration,
        CancellationToken cancellationToken)
    {
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
        try
        {
            int claimed = await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO auth_schema_migrations(id,applied_utc)
                VALUES(@id,@appliedUtc)
                ON CONFLICT(id) DO NOTHING;
                """,
                cancellationToken,
                ("@id", migration.Id),
                ("@appliedUtc", ToUtc(DateTimeOffset.UtcNow))).ConfigureAwait(false);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Inserts one complete user record.
    private static Task<int> InsertUserAsync(
        NpgsqlConnection connection,
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
            VALUES(@id,@email,@normalized,@passwordHash,@verified,@active,@failed,@lockout,@version,@created,@updated);
            """,
            cancellationToken,
            ("@id", user.Id),
            ("@email", user.Email),
            ("@normalized", user.NormalizedEmail),
            ("@passwordHash", user.PasswordHash),
            ("@verified", ToNullableUtc(user.EmailVerifiedUtc)),
            ("@active", user.IsActive),
            ("@failed", user.FailedLoginAttempts),
            ("@lockout", ToNullableUtc(user.LockoutEndUtc)),
            ("@version", user.SecurityVersion),
            ("@created", ToUtc(user.CreatedUtc)),
            ("@updated", ToUtc(user.UpdatedUtc)));

    // Inserts a one-time token hash and metadata.
    private static Task<int> InsertOneTimeTokenAsync(
        NpgsqlConnection connection,
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
            INSERT INTO {OneTimeTokenTable(purpose)}(id,user_id,purpose,token_hash,created_utc,expires_utc,consumed_utc)
            VALUES(@id,@userId,@purpose,@tokenHash,@created,@expires,NULL);
            """,
            cancellationToken,
            ("@id", Guid.NewGuid()),
            ("@userId", userId),
            ("@purpose", purpose),
            ("@tokenHash", tokenHash),
            ("@created", ToUtc(createdUtc)),
            ("@expires", ToUtc(expiresUtc)));

    // Finds one active one-time token inside an existing transaction.
    private static async Task<OneTimeTokenRecord?> FindActiveOneTimeTokenAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        string purpose,
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            $"""
            SELECT user_id,purpose,expires_utc
            FROM {OneTimeTokenTable(purpose)}
            WHERE purpose=@purpose AND token_hash=@tokenHash
              AND consumed_utc IS NULL AND expires_utc>@now
            LIMIT 1;
            """,
            ("@purpose", purpose),
            ("@tokenHash", tokenHash),
            ("@now", ToUtc(now)));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new OneTimeTokenRecord(reader.GetGuid(0), reader.GetString(1), ReadDate(reader, 2))
            : null;
    }

    // Marks one active one-time token consumed exactly once.
    private static Task<int> ConsumeTokenInternalAsync(
        NpgsqlConnection connection,
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
            SET consumed_utc=@now
            WHERE purpose=@purpose AND token_hash=@tokenHash
              AND consumed_utc IS NULL AND expires_utc>@now;
            """,
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@purpose", purpose),
            ("@tokenHash", tokenHash));

    // Maps a trusted internal token purpose to its dedicated provider-owned table.
    private static string OneTimeTokenTable(string purpose) => purpose switch
    {
        VerificationPurpose => "auth_email_verification_tokens",
        PasswordResetPurpose => "auth_password_reset_tokens",
        _ when purpose.StartsWith("oauth_exchange:", StringComparison.Ordinal) => "auth_oauth_exchange_codes",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), "Unsupported one-time token purpose.")
    };

    // Inserts a hashed refresh token and request metadata.
    private static Task<int> InsertRefreshTokenAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        RefreshTokenRecord token,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_refresh_tokens(
                id,user_id,token_hash,family_id,security_version,ip_address,user_agent,
                authenticated_utc,created_utc,expires_utc,revoked_utc,replaced_by_token_id)
            VALUES(@id,@userId,@tokenHash,@familyId,@version,@ip,@agent,@authenticated,@created,@expires,@revoked,@replacement);
            """,
            cancellationToken,
            ("@id", token.Id),
            ("@userId", token.UserId),
            ("@tokenHash", token.TokenHash),
            ("@familyId", token.FamilyId),
            ("@version", token.SecurityVersion),
            ("@ip", token.IpAddress),
            ("@agent", token.UserAgent),
            ("@authenticated", ToUtc(token.AuthenticatedUtc)),
            ("@created", ToUtc(token.CreatedUtc)),
            ("@expires", ToUtc(token.ExpiresUtc)),
            ("@revoked", ToNullableUtc(token.RevokedUtc)),
            ("@replacement", token.ReplacedByTokenId));

    // Finds a refresh token inside an existing transaction.
    private static async Task<RefreshTokenRecord?> FindRefreshTokenInternalAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            RefreshTokenSelect + " WHERE token_hash=@tokenHash LIMIT 1;",
            ("@tokenHash", tokenHash));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapRefreshToken(reader) : null;
    }

    // Revokes every active refresh token for one user inside an optional transaction.
    private static Task<int> RevokeUserTokensInternalAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_refresh_tokens SET revoked_utc=@now WHERE user_id=@userId AND revoked_utc IS NULL;",
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@userId", userId));

    // Revokes every active token in one family inside an optional transaction.
    private static Task<int> RevokeFamilyInternalAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_refresh_tokens SET revoked_utc=@now WHERE family_id=@familyId AND revoked_utc IS NULL;",
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@familyId", familyId));

    // Finds a user by ID inside an existing transaction.
    private static async Task<AuthUser?> FindUserInternalAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            UserSelect + " WHERE id=@userId LIMIT 1;",
            ("@userId", userId));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Finds a user by normalized email inside an existing transaction.
    private static async Task<AuthUser?> FindUserByNormalizedEmailInternalAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            UserSelect + " WHERE normalized_email=@normalized LIMIT 1;",
            ("@normalized", normalizedEmail));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Finds a user linked to an OAuth provider subject inside an existing transaction.
    private static async Task<AuthUser?> FindOAuthUserInternalAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        string provider,
        string subject,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            connection,
            transaction,
            """
            SELECT auth_users.id,auth_users.email,auth_users.normalized_email,auth_users.password_hash,
                   auth_users.email_verified_utc,auth_users.is_active,auth_users.failed_login_attempts,
                   auth_users.lockout_end_utc,auth_users.security_version,auth_users.created_utc,auth_users.updated_utc
            FROM auth_users
            INNER JOIN auth_oauth_accounts oa ON oa.user_id=auth_users.id
            WHERE oa.provider=@provider AND oa.provider_subject=@subject LIMIT 1;
            """,
            ("@provider", provider),
            ("@subject", subject));
        return await ReadSingleUserAsync(command, cancellationToken).ConfigureAwait(false);
    }

    // Inserts a tenant membership only when both active user and tenant exist.
    private static Task<int> InsertTenantMembershipInternalAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_tenant_memberships(tenant_id,user_id,created_utc)
            SELECT t.id,u.id,@created
            FROM auth_tenants t CROSS JOIN auth_users u
            WHERE t.id=@tenantId AND u.id=@userId AND u.is_active=true
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken,
            ("@created", ToUtc(now)),
            ("@tenantId", tenantId),
            ("@userId", userId));

    // Inserts a role assignment only when user, role, and optional tenant membership exist.
    private static Task<bool> AssignRoleInternalAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        AssignRoleCoreAsync(connection, transaction, userId, roleId, tenantId, now, cancellationToken);

    // Executes the role-assignment insert and reports whether a row was added.
    private static async Task<bool> AssignRoleCoreAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string sql = tenantId.HasValue
            ? """
              INSERT INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
              SELECT @id,m.tenant_id,m.user_id,r.id,@created
              FROM auth_tenant_memberships m
              INNER JOIN auth_tenant_roles r ON r.tenant_id=m.tenant_id
              WHERE m.tenant_id=@tenantId
                AND m.user_id=@userId
                AND r.id=@roleId
                AND r.id<>@ownerRoleId
              ON CONFLICT DO NOTHING;
              """
            : """
              INSERT INTO auth_global_user_roles(id,user_id,role_id,created_utc)
              SELECT @id,u.id,r.id,@created
              FROM auth_users u CROSS JOIN auth_global_roles r
              WHERE u.id=@userId AND r.id=@roleId AND u.is_active=true
              ON CONFLICT DO NOTHING;
              """;
        int affected = await ExecuteAsync(
            connection,
            transaction,
            sql,
            cancellationToken,
            ("@id", Guid.NewGuid()),
            ("@created", ToUtc(now)),
            ("@userId", userId),
            ("@roleId", roleId),
            ("@tenantId", tenantId),
            ("@ownerRoleId", TenantOwnerRoleId)).ConfigureAwait(false);
        return affected == 1;
    }

    // Invalidates access-token versions and refresh sessions for one user.
    private static async Task InvalidateUserSessionsInternalAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE auth_users SET security_version=security_version+1,updated_utc=@now WHERE id=@userId;",
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@userId", userId)).ConfigureAwait(false);
        await RevokeUserTokensInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
    }

    // Invalidates every user assigned to one role, including tenant-scoped assignments.
    private static async Task InvalidateRoleUsersInternalAsync(
        NpgsqlConnection connection,
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
            SET security_version=security_version+1,updated_utc=@now
            WHERE id IN (
                SELECT user_id FROM auth_global_user_roles WHERE role_id=@roleId
                UNION
                SELECT user_id FROM auth_tenant_user_roles WHERE role_id=@roleId
            );
            """,
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@roleId", roleId)).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=@now
            WHERE revoked_utc IS NULL
              AND user_id IN (
                  SELECT user_id FROM auth_global_user_roles WHERE role_id=@roleId
                  UNION
                  SELECT user_id FROM auth_tenant_user_roles WHERE role_id=@roleId
              );
            """,
            cancellationToken,
            ("@now", ToUtc(now)),
            ("@roleId", roleId)).ConfigureAwait(false);
    }

    // Reads one sequence of strings using user and optional tenant parameters.
    private async Task<IReadOnlyList<string>> ReadStringsAsync(
        string sql,
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        List<string> values = [];
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(
            connection,
            null,
            sql,
            ("@userId", userId),
            ("@tenantId", tenantId));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    // Reads one user from a prepared command.
    private static async Task<AuthUser?> ReadSingleUserAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapUser(reader) : null;
    }

    // Maps the standard user projection.
    private static AuthUser MapUser(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            ReadNullableString(reader, 3),
            ReadNullableDate(reader, 4),
            reader.GetBoolean(5),
            reader.GetInt32(6),
            ReadNullableDate(reader, 7),
            reader.GetInt32(8),
            ReadDate(reader, 9),
            ReadDate(reader, 10));

    // Maps the standard refresh-token projection.
    private static RefreshTokenRecord MapRefreshToken(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetInt32(4),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6),
            ReadDate(reader, 7),
            ReadDate(reader, 8),
            ReadDate(reader, 9),
            ReadNullableDate(reader, 10),
            ReadNullableGuid(reader, 11));

    // Maps a tenant projection.
    private static TenantRecord MapTenant(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), ReadDate(reader, 3));

    // Creates a parameterized PostgreSQL command for an optional transaction.
    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (NpgsqlTransaction?)transaction;
        foreach ((string name, object? value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return command;
    }

    // Executes one parameterized non-query asynchronously.
    private static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using NpgsqlCommand command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Adds a parameter without concatenating input into SQL.
    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        NpgsqlParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch
        {
            null => DBNull.Value,
            DateTimeOffset timestamp => ToUtc(timestamp),
            _ => value
        };
        command.Parameters.Add(parameter);
    }

    // Rolls back best-effort without hiding the original exception.
    private static async Task SafeRollbackAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbException)
        {
        }
    }

    // Identifies PostgreSQL constraint failures used for conflict-safe operations.
    private static bool IsConstraintViolation(PostgresException exception) => exception.SqlState is
        "23505"
        or "23503"
        or "23502"
        or "23514"
        or "23P01";

    // Normalizes a timestamp to UTC before sending it to PostgreSQL.
    private static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();

    // Normalizes a nullable timestamp to UTC for ADO.NET parameters.
    private static DateTimeOffset? ToNullableUtc(DateTimeOffset? value) => value.HasValue ? ToUtc(value.Value) : null;

    // Reads a PostgreSQL timestamp using common Npgsql materialization forms.
    private static DateTimeOffset ReadDate(DbDataReader reader, int ordinal)
    {
        object value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset timestamp => timestamp.ToUniversalTime(),
            DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)).ToUniversalTime(),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
            _ => throw new InvalidCastException($"Column {ordinal} is not a supported timestamp value.")
        };
    }

    // Reads a nullable string column.
    private static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // Reads a nullable timestamp column.
    private static DateTimeOffset? ReadNullableDate(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);

    // Reads a nullable GUID column.
    private static Guid? ReadNullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}
