using System.Data;
using System.Data.Common;

namespace SharpAccess.Persistence;

// Represents the provider-backed authentication database used by the application layer.
internal interface IAuthDatabase : IAuthStore, IAuthSchemaManager
{
}

// Opens provider connections without exposing a concrete ADO.NET provider to the core package.
internal interface IAuthConnectionFactory
{
    // Opens and initializes one provider connection.
    ValueTask<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}

// Describes the provider capabilities required by the authentication database boundary.
internal interface IAuthDatabaseProvider
{
    // Gets the stable provider name used for diagnostics and configuration validation.
    string Name { get; }

    // Gets the provider connection factory.
    IAuthConnectionFactory Connections { get; }

    // Gets the provider command factory.
    IAuthCommandFactory Commands { get; }

    // Gets the provider SQL-dialect adapter.
    IAuthSqlDialect Dialect { get; }

    // Gets the ordered provider migration source.
    IAuthMigrationProvider Migrations { get; }

    // Gets the provider transaction coordinator.
    IAuthTransactionManager Transactions { get; }
}

// Isolates SQL-dialect details that are shared by provider infrastructure.
internal interface IAuthSqlDialect
{
    // Normalizes a logical parameter name for the provider command syntax.
    string Parameter(string logicalName);
}

// Creates parameterized ADO.NET commands bound to an optional transaction.
internal interface IAuthCommandFactory
{
    // Creates a command for trusted provider-owned SQL.
    DbCommand Create(DbConnection connection, DbTransaction? transaction, string commandText);
}

// Describes one ordered provider-owned schema migration with an immutable checksum.
internal sealed record AuthMigration(string Id, string Sql)
{
    // Gets the line-ending-stable SHA-256 checksum used to detect modified migration history.
    internal string Checksum { get; } = AuthMigrationSupport.ComputeChecksum(Id, Sql);
}

// Supplies ordered migrations without placing provider SQL in the core package.
internal interface IAuthMigrationProvider
{
    // Gets migrations in deterministic application order.
    IReadOnlyList<AuthMigration> GetMigrations();
}

// Coordinates asynchronous provider transactions while preserving cancellation.
internal interface IAuthTransactionManager
{
    // Executes one operation inside a transaction and commits only on success.
    Task<T> ExecuteAsync<T>(
        DbConnection connection,
        IsolationLevel isolationLevel,
        Func<DbTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
