using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Sqlite.Migrations;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthDatabaseProvider(
    IAuthConnectionFactory connections,
    IAuthCommandFactory commands,
    IAuthSqlDialect dialect,
    IAuthMigrationProvider migrations,
    IAuthTransactionManager transactions) : IAuthDatabaseProvider
{
    // Gets the stable provider name.
    public string Name => "sqlite";

    // Gets the provider connection factory.
    public IAuthConnectionFactory Connections { get; } = connections;

    // Gets the provider command factory.
    public IAuthCommandFactory Commands { get; } = commands;

    // Gets the SQL-dialect adapter.
    public IAuthSqlDialect Dialect { get; } = dialect;

    // Gets the ordered migration source.
    public IAuthMigrationProvider Migrations { get; } = migrations;

    // Gets the transaction coordinator.
    public IAuthTransactionManager Transactions { get; } = transactions;
}
