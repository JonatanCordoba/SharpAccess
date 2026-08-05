using System.Data;
using System.Globalization;
using SharpAccess.Persistence;
using SharpAccess.Postgres;
using SharpAccess.Postgres.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresOperationalContractTests
{
    private const string RecoveryNormalizedEmail = "RECOVERY@SHARPACCESS.LOCAL";
    private const string RuntimeRole = "sharpaccess_phase8d_runtime";

    // Verifies provider-owned data sources apply UTC sessions and bounded connection settings.
    [PostgresFact]
    public async Task ProviderOwnedDataSourceAppliesUtcAndBoundedSettings()
    {
        NpgsqlConnectionStringBuilder builder = CreateBoundedBuilder(PostgresProviderContractTestSupport.RequireConnectionString());
        await using PostgresAuthConnectionFactory factory = new(new PostgresAuthOptions { ConnectionString = builder.ConnectionString });
        await using NpgsqlConnection connection = await factory.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        Assert.Equal(builder.CommandTimeout, command.CommandTimeout);
        command.CommandText = "SHOW TIME ZONE;";
        string timezone = Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Equal("UTC", timezone, ignoreCase: true);
        command.CommandText = "SHOW application_name;";
        Assert.Equal("SharpAccess.Postgres", Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty);
    }

    // Verifies caller cancellation interrupts a long-running PostgreSQL command.
    [PostgresFact]
    public async Task CancellationInterruptsLongRunningCommand()
    {
        NpgsqlConnectionStringBuilder builder = CreateBoundedBuilder(PostgresProviderContractTestSupport.RequireConnectionString());
        builder.Pooling = false;
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT pg_sleep(30);", connection) { CommandTimeout = 30 };
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await command.ExecuteScalarAsync(cancellation.Token); });
    }

    // Verifies command timeout failures map to the provider-neutral timeout category.
    [PostgresFact]
    public async Task CommandTimeoutMapsToProviderNeutralTimeout()
    {
        NpgsqlConnectionStringBuilder builder = CreateBoundedBuilder(PostgresProviderContractTestSupport.RequireConnectionString());
        builder.Pooling = false;
        await using NpgsqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT pg_sleep(3);", connection) { CommandTimeout = 1 };
        Exception? exception = await Record.ExceptionAsync(async () => { await command.ExecuteScalarAsync(); });
        Assert.NotNull(exception);
        Assert.Equal(AuthDatabaseErrorCategory.Timeout, new PostgresAuthDatabaseErrorClassifier().Classify(exception!));
    }

    // Verifies PostgreSQL native UUID, timestamptz, and boolean values round-trip without lossy conversion.
    [PostgresFact]
    public async Task NativeTypesRoundTripWithoutLoss()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (NpgsqlCommand create = new("CREATE TEMP TABLE sharpaccess_type_roundtrip(id uuid NOT NULL,occurred_utc timestamptz NOT NULL,enabled boolean NOT NULL) ON COMMIT DROP;", connection, transaction))
        {
            await create.ExecuteNonQueryAsync();
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset occurredUtc = new(2026, 7, 20, 15, 30, 0, TimeSpan.Zero);
        await using (NpgsqlCommand insert = new("INSERT INTO sharpaccess_type_roundtrip(id,occurred_utc,enabled) VALUES(@id,@occurred,true);", connection, transaction))
        {
            insert.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, id);
            insert.Parameters.AddWithValue("occurred", NpgsqlDbType.TimestampTz, occurredUtc.UtcDateTime);
            await insert.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand read = new("SELECT id,occurred_utc,enabled FROM sharpaccess_type_roundtrip;", connection, transaction);
        await using NpgsqlDataReader reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(id, reader.GetGuid(0));
        Assert.Equal(occurredUtc.UtcDateTime, reader.GetDateTime(1).ToUniversalTime());
        Assert.True(reader.GetBoolean(2));
        await reader.DisposeAsync();
        await transaction.RollbackAsync();
    }

    // Verifies the PostgreSQL advisory migration lock fails closed and recovers after release.
    [PostgresFact]
    public async Task AdvisoryMigrationLockFailsClosedAndRecovers()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString);
        await using NpgsqlConnection lockConnection = new(connectionString);
        await lockConnection.OpenAsync();
        await using NpgsqlTransaction lockTransaction = await lockConnection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using (NpgsqlCommand lockCommand = new($"SELECT pg_advisory_xact_lock({PostgresAuthMigrationDialect.MigrationLockKey});", lockConnection, lockTransaction))
        {
            await lockCommand.ExecuteScalarAsync();
        }

        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        IAuthSchemaManager schema = scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>();
        await Assert.ThrowsAsync<TimeoutException>(() => schema.MigrateAsync());
        await lockTransaction.RollbackAsync();
        await schema.MigrateAsync();
        Assert.True((await schema.GetStatusAsync()).IsCurrent);
    }

    // Verifies the legacy PostgreSQL 001-through-004 schema upgrades to the immutable current catalog.
    [PostgresFact]
    public async Task LegacyPreScopeSchemaUpgradesToCurrentCatalog()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString);
        await using (NpgsqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand ledger = new("CREATE TABLE auth_schema_migrations(id text PRIMARY KEY,applied_utc timestamptz NOT NULL);", connection);
            await ledger.ExecuteNonQueryAsync();
            foreach (PostgresMigration migration in PostgresMigrations.All)
            {
                await using NpgsqlCommand migrationCommand = new(migration.Sql, connection);
                await migrationCommand.ExecuteNonQueryAsync();
                await using NpgsqlCommand record = new("INSERT INTO auth_schema_migrations(id,applied_utc) VALUES(@id,CURRENT_TIMESTAMP);", connection);
                record.Parameters.AddWithValue("id", migration.Id);
                await record.ExecuteNonQueryAsync();
            }
        }

        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        IAuthSchemaManager schema = scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>();
        await schema.MigrateAsync();
        SharpAccessSchemaStatus status = await schema.GetStatusAsync();
        Assert.True(status.IsCurrent);
        Assert.Equal(scope.ServiceProvider.GetRequiredService<IAuthMigrationProvider>().GetMigrations().Count, status.AppliedMigrations.Count);
    }

    // Verifies generated PostgreSQL migration scripts use a bounded transaction-level advisory lock.
    [PostgresFact]
    public async Task GeneratedMigrationScriptUsesBoundedAdvisoryLock()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString);
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        string script = await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>().GenerateScriptAsync();
        Assert.Contains("SET LOCAL lock_timeout = '30s';", script, StringComparison.Ordinal);
        Assert.Contains($"pg_advisory_xact_lock({PostgresAuthMigrationDialect.MigrationLockKey})", script, StringComparison.Ordinal);
    }

    // Verifies the stable user-page query can use the matching PostgreSQL keyset index.
    [PostgresFact]
    public async Task UserPageQueryUsesKeysetIndex()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString);
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>().MigrateAsync();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (NpgsqlCommand settings = new("SET LOCAL enable_seqscan=off;", connection, transaction))
        {
            await settings.ExecuteNonQueryAsync();
        }

        await using NpgsqlCommand explain = new("EXPLAIN (FORMAT JSON,COSTS OFF) SELECT id,created_utc FROM auth_users ORDER BY created_utc DESC,id ASC LIMIT 26;", connection, transaction);
        string plan = Convert.ToString(await explain.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
        Assert.Contains("ix_auth_users_created", plan, StringComparison.Ordinal);
        await transaction.RollbackAsync();
    }

    // Verifies a runtime-only PostgreSQL principal validates schema but cannot issue DDL.
    [PostgresReadinessFact]
    public async Task RestrictedRuntimePrincipalValidatesButCannotCreateTables()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString);
        await using ServiceProvider migrationProvider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using (IServiceScope scope = migrationProvider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>().MigrateAsync();
        }

        ServiceProvider? runtimeProvider = null;
        try
        {
            await ConfigureRuntimeRoleAsync(connectionString);
            ServiceCollection services = new();
            services.AddPostgresAccess(token => OpenRestrictedConnectionAsync(connectionString, token));
            runtimeProvider = services.BuildServiceProvider(validateScopes: true);
            using IServiceScope runtimeScope = runtimeProvider.CreateScope();
            await runtimeScope.ServiceProvider.GetRequiredService<IAuthSchemaManager>().ValidateAsync();
            await using NpgsqlConnection restricted = await OpenRestrictedConnectionAsync(connectionString, CancellationToken.None);
            await using NpgsqlCommand forbidden = new("CREATE TABLE sharpaccess_runtime_must_not_create(id integer);", restricted);
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(async () => { await forbidden.ExecuteNonQueryAsync(); });
            Assert.Equal("42501", exception.SqlState);
        }
        finally
        {
            if (runtimeProvider is not null)
            {
                await runtimeProvider.DisposeAsync();
            }
            await RemoveRuntimeRoleAsync(connectionString);
        }
    }

    // Verifies the restored recovery database contains current migration evidence and the seeded account.
    [PostgresRecoveryFact]
    public async Task RestoredDatabaseContainsCurrentSchemaAndRecoveryUser()
    {
        string connectionString = RequireRecoveryConnectionString();
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>().ValidateAsync();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT COUNT(*) FROM auth_users WHERE normalized_email=@email AND is_active=true;", connection);
        command.Parameters.AddWithValue("email", RecoveryNormalizedEmail);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
    }

    // Creates bounded provider-owned settings from the selected scratch database connection string.
    private static NpgsqlConnectionStringBuilder CreateBoundedBuilder(string connectionString) => new(connectionString)
    {
        Timeout = 5,
        CommandTimeout = 5,
        CancellationTimeout = 1_000,
        MinPoolSize = 0,
        MaxPoolSize = 10,
        Pooling = true
    };

    // Creates the no-login runtime role and grants only data access to current SharpAccess tables.
    private static async Task ConfigureRuntimeRoleAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand currentUserCommand = new("SELECT current_user;", connection);
        string currentUser = Convert.ToString(await currentUserCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The PostgreSQL current user could not be resolved.");
        using NpgsqlCommandBuilder commandBuilder = new();
        string quotedCurrentUser = commandBuilder.QuoteIdentifier(currentUser);
        string sql = $"""
            DO $cleanup$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{RuntimeRole}') THEN
                    EXECUTE 'REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM {RuntimeRole}';
                    EXECUTE 'REVOKE ALL PRIVILEGES ON SCHEMA public FROM {RuntimeRole}';
                    EXECUTE 'REVOKE {RuntimeRole} FROM ' || quote_ident(current_user);
                    EXECUTE 'DROP ROLE {RuntimeRole}';
                END IF;
            END
            $cleanup$;
            CREATE ROLE {RuntimeRole} NOLOGIN;
            GRANT {RuntimeRole} TO {quotedCurrentUser};
            GRANT USAGE ON SCHEMA public TO {RuntimeRole};
            GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO {RuntimeRole};
            """;
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // Opens one logical connection whose session is restricted to the runtime-only role.
    private static async ValueTask<NpgsqlConnection> OpenRestrictedConnectionAsync(string connectionString, CancellationToken cancellationToken)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString) { Pooling = false };
        NpgsqlConnection connection = new(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = new($"SET ROLE {RuntimeRole};", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Revokes and removes the temporary runtime-only readiness role.
    private static async Task RemoveRuntimeRoleAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        string sql = $"""
            DO $cleanup$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='{RuntimeRole}') THEN
                    EXECUTE 'REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM {RuntimeRole}';
                    EXECUTE 'REVOKE ALL PRIVILEGES ON SCHEMA public FROM {RuntimeRole}';
                    EXECUTE 'REVOKE {RuntimeRole} FROM ' || quote_ident(current_user);
                    EXECUTE 'DROP ROLE {RuntimeRole}';
                END IF;
            END
            $cleanup$;
            """;
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // Reads the restored-database connection without allowing destructive reset behavior.
    private static string RequireRecoveryConnectionString()
    {
        string connectionString = Environment.GetEnvironmentVariable(PostgresProviderContractTestSupport.ConnectionStringEnvironmentVariable) ?? string.Empty;
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        string database = builder.Database ?? string.Empty;
        if (!database.StartsWith("sharpaccess_contract_tests_", StringComparison.Ordinal)
            || !database.EndsWith("_restored", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The PostgreSQL recovery verification database must use the approved restored scratch name.");
        }
        return connectionString;
    }
}

internal sealed class PostgresReadinessFactAttribute : FactAttribute
{
    // Skips promotion-readiness evidence unless the explicit PostgreSQL readiness flag is enabled.
    public PostgresReadinessFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SHARPACCESS_POSTGRES_READINESS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set SHARPACCESS_POSTGRES_READINESS=true to run restricted-principal PostgreSQL readiness evidence.";
        }
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresProviderContractTestSupport.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {PostgresProviderContractTestSupport.ConnectionStringEnvironmentVariable} to run PostgreSQL readiness evidence.";
        }
    }
}

internal sealed class PostgresRecoveryFactAttribute : FactAttribute
{
    // Skips restored-database verification unless the recovery runner explicitly enables it.
    public PostgresRecoveryFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SHARPACCESS_POSTGRES_RECOVERY_VERIFY"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "PostgreSQL restored-database verification is invoked only by the recovery drill.";
        }
    }
}
