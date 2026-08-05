using System.Globalization;
using SharpAccess;
using SharpAccess.Domain;
using SharpAccess.Sqlite;
using SharpAccess.Sqlite.Migrations;
using Microsoft.Data.Sqlite;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
[Trait("Capability", "MigrationContract")]
public sealed class SqliteAuthorizationScopeUpgradeTests : IAsyncLifetime
{
    private static readonly Guid OwnerUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RefreshTokenId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RefreshFamilyId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset CreatedUtc = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sharpaccess-authorization-upgrade-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    // Verifies that a pre-split database receives isolated catalogs, one owner, the Owner role, and forced reauthentication.
    [Fact]
    public async Task PreSplitDatabaseMigratesWithoutCrossScopeAuthority()
    {
        string connectionString = $"Data Source={_databasePath};Pooling=False;Foreign Keys=True";
        await CreatePreSplitDatabaseAsync(connectionString);

        SqliteAuthStore store = new(new TestSqliteConnectionFactory(connectionString));
        await store.InitializeAsync();

        EffectiveAuthorizationContext context = await store.GetEffectiveAuthorizationContextAsync(
            OwnerUserId,
            TenantId);

        Assert.Contains(AuthRoles.User, context.Global.Roles);
        Assert.DoesNotContain(AuthPermissions.TenantsRead, context.Global.Permissions);
        Assert.NotNull(context.Tenant);
        Assert.True(context.Tenant.IsOwner);
        Assert.Contains(TenantAuthRoles.Owner, context.Tenant.Roles);
        Assert.Contains(TenantAuthPermissions.OwnershipTransfer, context.Tenant.Permissions);
        Assert.DoesNotContain(AuthRoles.Admin, context.Tenant.Roles);

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='auth_user_roles';"));
        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM auth_tenant_owners WHERE tenant_id=$tenantId AND user_id=$userId;",
            ("$tenantId", TenantId.ToString("D")),
            ("$userId", OwnerUserId.ToString("D"))));
        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            """
            SELECT COUNT(*)
            FROM auth_tenant_user_roles
            WHERE tenant_id=$tenantId AND user_id=$userId
              AND role_id='40000000-0000-0000-0000-000000000001';
            """,
            ("$tenantId", TenantId.ToString("D")),
            ("$userId", OwnerUserId.ToString("D"))));
        Assert.Equal(0L, await ScalarInt64Async(
            connection,
            """
            SELECT COUNT(*)
            FROM auth_global_role_permissions
            WHERE role_id='10000000-0000-0000-0000-000000000002'
              AND permission_id='20000000-0000-0000-0000-000000000009';
            """));
        Assert.Equal(2L, await ScalarInt64Async(
            connection,
            "SELECT security_version FROM auth_users WHERE id=$userId;",
            ("$userId", OwnerUserId.ToString("D"))));
        Assert.Equal(1L, await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM auth_refresh_tokens WHERE id=$id AND revoked_utc IS NOT NULL;",
            ("$id", RefreshTokenId.ToString("D"))));
    }

    // Creates the exact historical schema through migration 004 and inserts one tenant owner candidate with an active session.
    private static async Task CreatePreSplitDatabaseAsync(string connectionString)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            """
            CREATE TABLE auth_schema_migrations(
                id TEXT PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """);

        foreach (SqliteMigration migration in SqliteMigrations.All)
        {
            await ExecuteAsync(connection, migration.Sql);
            await ExecuteAsync(
                connection,
                "INSERT INTO auth_schema_migrations(id,applied_utc) VALUES($id,$applied);",
                ("$id", migration.Id),
                ("$applied", Format(CreatedUtc)));
        }

        await ExecuteAsync(
            connection,
            """
            INSERT INTO auth_users(
                id,email,normalized_email,password_hash,email_verified_utc,is_active,
                failed_login_attempts,lockout_end_utc,security_version,created_utc,updated_utc)
            VALUES($userId,'owner@example.com','OWNER@EXAMPLE.COM','hash',$created,1,0,NULL,1,$created,$created);

            INSERT INTO auth_tenants(id,name,slug,created_utc)
            VALUES($tenantId,'Upgrade Tenant','upgrade-tenant',$created);

            INSERT INTO auth_tenant_memberships(tenant_id,user_id,created_utc)
            VALUES($tenantId,$userId,$created);

            INSERT INTO auth_user_roles(id,user_id,role_id,tenant_id,created_utc)
            VALUES
                ('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',$userId,'10000000-0000-0000-0000-000000000002',NULL,$created),
                ('ffffffff-ffff-ffff-ffff-ffffffffffff',$userId,'10000000-0000-0000-0000-000000000001',$tenantId,$created);

            INSERT INTO auth_security_audit_logs(
                id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail)
            VALUES(
                '11111111-1111-1111-1111-111111111111',$created,'tenant_created',$userId,$tenantId,
                '127.0.0.1','upgrade-test','owner backfill source');

            INSERT INTO auth_refresh_tokens(
                id,user_id,token_hash,family_id,security_version,ip_address,user_agent,
                created_utc,expires_utc,revoked_utc,replaced_by_token_id)
            VALUES(
                $refreshId,$userId,'upgrade-refresh-hash',$familyId,1,'127.0.0.1','upgrade-test',
                $created,$expires,NULL,NULL);
            """,
            ("$userId", OwnerUserId.ToString("D")),
            ("$tenantId", TenantId.ToString("D")),
            ("$refreshId", RefreshTokenId.ToString("D")),
            ("$familyId", RefreshFamilyId.ToString("D")),
            ("$created", Format(CreatedUtc)),
            ("$expires", Format(CreatedUtc.AddDays(30))));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object parameterValue) in parameters)
        {
            command.Parameters.AddWithValue(name, parameterValue);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object parameterValue) in parameters)
        {
            command.Parameters.AddWithValue(name, parameterValue);
        }

        object? scalarValue = await command.ExecuteScalarAsync();
        return Convert.ToInt64(scalarValue, CultureInfo.InvariantCulture);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed class TestSqliteConnectionFactory(string connectionString) : ISqliteAuthConnectionFactory
    {
        public async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            SqliteConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
    }
}
