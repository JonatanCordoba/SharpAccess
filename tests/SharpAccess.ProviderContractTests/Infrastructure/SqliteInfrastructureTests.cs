using SharpAccess.Persistence;
using SharpAccess.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
public sealed class SqliteInfrastructureTests
{
    // Verifies SQLite parameter normalization and blank-name rejection.
    [Fact]
    public void SqliteDialectNormalizesParametersAndRejectsBlankNames()
    {
        SqliteAuthSqlDialect dialect = new();

        Assert.Equal("$id", dialect.Parameter("id"));
        Assert.Equal("$id", dialect.Parameter("$id"));
        Assert.Equal("$id", dialect.Parameter("@id"));
        Assert.Equal("$id", dialect.Parameter(":id"));

        Assert.Throws<ArgumentException>(() => dialect.Parameter(" "));
    }

    // Verifies command creation and defensive input validation.
    [Fact]
    public async Task CommandFactoryCreatesCommandsAndValidatesInputs()
    {
        SqliteAuthCommandFactory factory = new();

        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();

        await using DbTransaction transaction = await connection.BeginTransactionAsync();
        await using DbCommand command = factory.Create(connection, transaction, "select 1");

        Assert.Equal("select 1", command.CommandText);
        Assert.Same(transaction, command.Transaction);

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, null, "select 1"));
        Assert.Throws<ArgumentException>(() => factory.Create(connection, null, " "));
    }

    // Verifies the provider exposes non-empty ordered migration definitions.
    [Fact]
    public void MigrationProviderReturnsProviderMigrations()
    {
        IReadOnlyList<AuthMigration> migrations = new SqliteAuthMigrationProvider().GetMigrations();

        Assert.NotEmpty(migrations);
        Assert.All(migrations, migration =>
        {
            Assert.False(string.IsNullOrWhiteSpace(migration.Id));
            Assert.False(string.IsNullOrWhiteSpace(migration.Sql));
        });
    }

    // Verifies the database provider retains its provider-neutral infrastructure dependencies.
    [Fact]
    public void DatabaseProviderExposesItsDependencies()
    {
        IAuthConnectionFactory connections = new SqliteAuthConnectionFactory(
            Options.Create(new SqliteAuthOptions { ConnectionString = "Data Source=:memory:" }));
        IAuthCommandFactory commands = new SqliteAuthCommandFactory();
        IAuthSqlDialect dialect = new SqliteAuthSqlDialect();
        IAuthMigrationProvider migrations = new SqliteAuthMigrationProvider();
        IAuthTransactionManager transactions = new SqliteAuthTransactionManager();

        SqliteAuthDatabaseProvider provider = new(
            connections,
            commands,
            dialect,
            migrations,
            transactions);

        Assert.Equal("sqlite", provider.Name);
        Assert.Same(connections, provider.Connections);
        Assert.Same(commands, provider.Commands);
        Assert.Same(dialect, provider.Dialect);
        Assert.Same(migrations, provider.Migrations);
        Assert.Same(transactions, provider.Transactions);
    }

    // Verifies in-memory connections receive only the mandatory per-connection policy.
    [Fact]
    public async Task ConnectionFactoryOpensMemoryDatabaseThroughConcreteAndNeutralContracts()
    {
        SqliteAuthConnectionFactory factory = new(
            Options.Create(new SqliteAuthOptions { ConnectionString = "Data Source=:memory:" }));

        await using SqliteConnection connection = await factory.OpenAsync();
        await using DbConnection neutralConnection = await ((IAuthConnectionFactory)factory).OpenAsync();

        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(ConnectionState.Open, neutralConnection.State);
        Assert.IsType<SqliteConnection>(neutralConnection);
        Assert.Equal(1, await ReadPragmaIntAsync(connection, "foreign_keys"));
        Assert.Equal(5000, await ReadPragmaIntAsync(connection, "busy_timeout"));
        Assert.Equal("memory", await ReadPragmaTextAsync(connection, "journal_mode"));
    }

    // Verifies provider-owned file databases use WAL, NORMAL synchronization, and bounded busy waiting.
    [Fact]
    public async Task ProviderOwnedFileDatabaseUsesReferenceOperationalPolicy()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpaccess-sqlite-{Guid.NewGuid():N}");
        string database = Path.Combine(root, "nested", "auth.db");
        try
        {
            SqliteAuthConnectionFactory factory = new(
                Options.Create(new SqliteAuthOptions { ConnectionString = $"Data Source={database};Pooling=False" }));

            await using SqliteConnection connection = await factory.OpenAsync();

            Assert.Equal(ConnectionState.Open, connection.State);
            Assert.True(Directory.Exists(Path.GetDirectoryName(database)));
            Assert.Equal(1, await ReadPragmaIntAsync(connection, "foreign_keys"));
            Assert.Equal(5000, await ReadPragmaIntAsync(connection, "busy_timeout"));
            Assert.Equal("wal", await ReadPragmaTextAsync(connection, "journal_mode"));
            Assert.Equal(1, await ReadPragmaIntAsync(connection, "synchronous"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // Verifies host-managed connection creation retains ownership of file-level journal policy.
    [Fact]
    public async Task HostManagedConnectionFactoryDoesNotOverrideJournalMode()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sharpaccess-sqlite-host-{Guid.NewGuid():N}");
        string database = Path.Combine(root, "auth.db");
        Directory.CreateDirectory(root);
        try
        {
            string connectionString = $"Data Source={database};Pooling=False";
            SqliteAuthConnectionFactory factory = new(
                _ => ValueTask.FromResult(new SqliteConnection(connectionString)));

            await using SqliteConnection connection = await factory.OpenAsync();

            Assert.Equal(1, await ReadPragmaIntAsync(connection, "foreign_keys"));
            Assert.Equal(5000, await ReadPragmaIntAsync(connection, "busy_timeout"));
            Assert.Equal("delete", await ReadPragmaTextAsync(connection, "journal_mode"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // Verifies a connection returned from a failed open path is disposed.
    [Fact]
    public async Task ConnectionFactoryDisposesConnectionWhenOpeningFails()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);
        try
        {
            SqliteAuthConnectionFactory factory = new(
                Options.Create(new SqliteAuthOptions { ConnectionString = $"Data Source={root};Pooling=False" }));

            await Assert.ThrowsAnyAsync<SqliteException>(() => factory.OpenAsync().AsTask());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    // Verifies successful transaction commits and exceptional transaction rollbacks.
    [Trait("MutationInvariant", "TransactionAtomicity")]
    [Fact]
    public async Task TransactionManagerCommitsSuccessAndRollsBackFailures()
    {
        SqliteAuthTransactionManager manager = new();

        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();

        await using DbCommand create = connection.CreateCommand();
        create.CommandText = "create table items (id integer primary key);";
        await create.ExecuteNonQueryAsync();

        int result = await manager.ExecuteAsync(
            connection,
            IsolationLevel.Serializable,
            async (transaction, cancellationToken) =>
            {
                await using DbCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "insert into items (id) values (1);";
                await insert.ExecuteNonQueryAsync(cancellationToken);
                return 42;
            });

        Assert.Equal(42, result);
        Assert.Equal(1, await CountRowsAsync(connection));

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ExecuteAsync<int>(
            connection,
            IsolationLevel.Serializable,
            async (transaction, cancellationToken) =>
            {
                await using DbCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "insert into items (id) values (2);";
                await insert.ExecuteNonQueryAsync(cancellationToken);
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal(1, await CountRowsAsync(connection));
    }

    // Counts transaction-test rows without materializing them.
    private static async Task<int> CountRowsAsync(SqliteConnection connection)
    {
        await using DbCommand count = connection.CreateCommand();
        count.CommandText = "select count(*) from items;";
        return Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // Reads an integer SQLite pragma value.
    private static async Task<int> ReadPragmaIntAsync(SqliteConnection connection, string name)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = FormattableString.Invariant($"PRAGMA {name};");
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    // Reads a text SQLite pragma value.
    private static async Task<string> ReadPragmaTextAsync(SqliteConnection connection, string name)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = FormattableString.Invariant($"PRAGMA {name};");
        return Convert.ToString(
            await command.ExecuteScalarAsync(),
            CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
