using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SharpAccess.Persistence;
using SharpAccess.Sqlite;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
[Trait("Capability", "MigrationContract")]
public sealed class SqliteMigrationContractTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sharpaccess-migration-contract-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    // Deletes the isolated SQLite database after each contract run.
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
        return Task.CompletedTask;
    }

    // Verifies clean migration, idempotency, reconciliation reporting, and host-object preservation.
    [Fact]
    public async Task MigrateIsIdempotentAndPreservesHostObjects()
    {
        string connectionString = ConnectionString();
        await using (SqliteConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "CREATE TABLE host_orders(id INTEGER PRIMARY KEY,description TEXT NOT NULL);");
            await ExecuteAsync(connection, "INSERT INTO host_orders(description) VALUES('preserve-me');");
        }

        await using ServiceProvider services = BuildServices(connectionString);
        await services.MigrateSharpAccessAsync();
        await services.MigrateSharpAccessAsync();
        SharpAccessSchemaStatus status = await services.GetSharpAccessSchemaStatusAsync();

        Assert.True(status.IsCurrent);
        Assert.Equal(12, status.AppliedMigrations.Count);
        await services.ValidateSharpAccessSchemaAsync();
        await using SqliteConnection verification = new(connectionString);
        await verification.OpenAsync();
        Assert.Equal(1L, await ScalarAsync(verification, "SELECT COUNT(*) FROM host_orders WHERE description='preserve-me';"));
        Assert.Equal(1L, await ScalarAsync(verification, "SELECT COUNT(*) FROM auth_migration_reconciliation_reports WHERE migration_id='009_record_authorization_reconciliation';"));
        Assert.Equal("created_utc:1,id:0", await IndexDefinitionAsync(verification, "ix_auth_audit_created"));
        Assert.Equal("created_utc:1,id:0", await IndexDefinitionAsync(verification, "ix_auth_global_roles_page"));
        Assert.Equal("created_utc:1,id:0", await IndexDefinitionAsync(verification, "ix_auth_global_permissions_page"));
        Assert.Equal("user_id:0,created_utc:1,tenant_id:0", await IndexDefinitionAsync(verification, "ix_auth_tenant_memberships_user_page"));
        Assert.Equal("tenant_id:0,created_utc:1,user_id:0", await IndexDefinitionAsync(verification, "ix_auth_tenant_memberships_tenant_page"));
    }

    // Verifies an immutable Phase 1-era database upgrades through every later migration without losing account ownership.
    [Fact]
    public async Task ImmutableHistoricalFixtureUpgradesToCurrentSchema()
    {
        string fixturePath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "migrations",
            "sqlite",
            "pre-phase1-through-004.db");
        File.Copy(fixturePath, _databasePath, overwrite: true);

        await using ServiceProvider services = BuildServices(ConnectionString());
        await services.MigrateSharpAccessAsync();
        SharpAccessSchemaStatus status = await services.GetSharpAccessSchemaStatusAsync();

        Assert.True(status.IsCurrent);
        await using SqliteConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT security_version FROM auth_users WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM auth_tenant_owners WHERE tenant_id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' AND user_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM auth_refresh_tokens WHERE id='cccccccc-cccc-cccc-cccc-cccccccccccc' AND revoked_utc IS NOT NULL;"));
    }

    // Verifies that concurrent startup attempts converge on one complete ledger.
    [Fact]
    public async Task ConcurrentMigrateCallsConverge()
    {
        string connectionString = ConnectionString();
        await using ServiceProvider services = BuildServices(connectionString);
        Task[] attempts = Enumerable.Range(0, 8)
            .Select(_ => services.MigrateSharpAccessAsync())
            .ToArray();

        await Task.WhenAll(attempts);
        SharpAccessSchemaStatus status = await services.GetSharpAccessSchemaStatusAsync();
        Assert.True(status.IsCurrent);
        Assert.Equal(status.AppliedMigrations.Count, status.AppliedMigrations.Distinct(StringComparer.Ordinal).Count());
    }

    // Verifies that checksum tampering is detected before another migration is applied.
    [Fact]
    public async Task ModifiedMigrationChecksumFailsValidation()
    {
        string connectionString = ConnectionString();
        await using ServiceProvider services = BuildServices(connectionString);
        await services.MigrateSharpAccessAsync();
        await using (SqliteConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(
                connection,
                "UPDATE auth_schema_migration_checksums SET checksum=@checksum WHERE id='001_initial_schema';",
                ("@checksum", new string('0', 64)));
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => services.ValidateSharpAccessSchemaAsync());
        Assert.Contains("checksum-mismatches=[001_initial_schema]", exception.Message, StringComparison.Ordinal);
    }

    // Verifies that validation succeeds through a read-only runtime connection after migration deployment.
    [Fact]
    public async Task ValidateOnlyWorksWithReadOnlyConnection()
    {
        await using (ServiceProvider migrationServices = BuildServices(ConnectionString()))
        {
            await migrationServices.MigrateSharpAccessAsync();
        }

        SqliteConnectionStringBuilder builder = new(ConnectionString())
        {
            Mode = SqliteOpenMode.ReadOnly
        };
        await using ServiceProvider runtimeServices = BuildServices(builder.ConnectionString);
        await runtimeServices.ValidateSharpAccessSchemaAsync();
        Assert.True((await runtimeServices.GetSharpAccessSchemaStatusAsync()).IsCurrent);
    }

    // Verifies that generated scripts contain ledgers, checksums, provider locking, and the latest migration.
    [Fact]
    public async Task CleanDatabaseScriptContainsCompleteCatalog()
    {
        await using ServiceProvider services = BuildServices(ConnectionString());
        string script = await services.GenerateSharpAccessMigrationScriptAsync();

        Assert.Contains("BEGIN IMMEDIATE", script, StringComparison.Ordinal);
        Assert.Contains("auth_schema_migration_checksums", script, StringComparison.Ordinal);
        Assert.Contains("009_record_authorization_reconciliation", script, StringComparison.Ordinal);
        Assert.Contains("010_token_hash_key_versions", script, StringComparison.Ordinal);
        Assert.Contains("011_refresh_token_authenticated_utc", script, StringComparison.Ordinal);
        Assert.Contains("012_pagination_indexes", script, StringComparison.Ordinal);
        Assert.Contains("COMMIT", script, StringComparison.Ordinal);
    }

    // Verifies that a failing migration rolls back its DDL and ledger claim and can be retried safely.
    [Fact]
    public async Task InterruptedTransactionalMigrationCanRecover()
    {
        string connectionString = ConnectionString();
        Func<CancellationToken, ValueTask<DbConnection>> open = async cancellationToken =>
        {
            SqliteConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        };
        AuthMigrationManager failing = new(
            open,
            new TestMigrationProvider(
                new AuthMigration("001_fixture", "CREATE TABLE auth_fixture(id INTEGER PRIMARY KEY);"),
                new AuthMigration("002_failure", "CREATE TABLE auth_fixture(id INTEGER PRIMARY KEY);")),
            new SqliteAuthMigrationDialect());

        await Assert.ThrowsAnyAsync<SqliteException>(() => failing.MigrateAsync());
        await using (SqliteConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='auth_fixture';"));
        }

        AuthMigrationManager recovered = new(
            open,
            new TestMigrationProvider(new AuthMigration("001_fixture", "CREATE TABLE auth_fixture(id INTEGER PRIMARY KEY);")),
            new SqliteAuthMigrationDialect());
        await recovered.MigrateAsync();
        Assert.True((await recovered.GetStatusAsync()).IsCurrent);
    }

    // Creates one scoped SQLite provider for the selected connection string.
    private static ServiceProvider BuildServices(string connectionString)
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = connectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    // Creates the isolated file-backed SQLite connection string.
    private string ConnectionString() => $"Data Source={_databasePath};Pooling=False;Foreign Keys=True";

    // Executes one parameterized SQLite statement.
    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    // Reads one invariant integer scalar from SQLite.
    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // Reads an ordered SQLite index signature as column-name and descending-flag pairs.
    private static async Task<string> IndexDefinitionAsync(SqliteConnection connection, string indexName)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT group_concat(name || ':' || "desc", ',')
            FROM (
                SELECT name,"desc"
                FROM pragma_index_xinfo(@indexName)
                WHERE key=1
                ORDER BY seqno
            );
            """;
        command.Parameters.AddWithValue("@indexName", indexName);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // Locates the repository root for immutable fixture access.
    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpAccess.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed class TestMigrationProvider(params AuthMigration[] migrations) : IAuthMigrationProvider
    {
        // Returns the immutable test migration sequence.
        public IReadOnlyList<AuthMigration> GetMigrations() => migrations;
    }
}
