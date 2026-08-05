using SharpAccess.Persistence;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthProviderComponents
{
    // Creates one immutable set of PostgreSQL provider infrastructure components.
    internal PostgresAuthProviderComponents(
        IPostgresAuthConnectionFactory connections,
        IAuthCommandFactory commands,
        IAuthSqlDialect dialect,
        IAuthMigrationProvider migrations,
        IAuthTransactionManager transactions,
        IAuthDatabaseProvider databaseProvider,
        IAuthSchemaManager schemaManager,
        PostgresAuthorizationStore authorizationStore)
    {
        Connections = connections;
        Commands = commands;
        Dialect = dialect;
        Migrations = migrations;
        Transactions = transactions;
        DatabaseProvider = databaseProvider;
        SchemaManager = schemaManager;
        AuthorizationStore = authorizationStore;
    }

    // Gets the PostgreSQL connection factory.
    internal IPostgresAuthConnectionFactory Connections { get; }

    // Gets the provider-neutral command factory.
    internal IAuthCommandFactory Commands { get; }

    // Gets the PostgreSQL SQL dialect adapter.
    internal IAuthSqlDialect Dialect { get; }

    // Gets the PostgreSQL migration provider.
    internal IAuthMigrationProvider Migrations { get; }

    // Gets the PostgreSQL transaction manager.
    internal IAuthTransactionManager Transactions { get; }

    // Gets the PostgreSQL database provider descriptor.
    internal IAuthDatabaseProvider DatabaseProvider { get; }

    // Gets the PostgreSQL schema manager.
    internal IAuthSchemaManager SchemaManager { get; }

    // Gets the PostgreSQL authorization store slice.
    internal PostgresAuthorizationStore AuthorizationStore { get; }
}
