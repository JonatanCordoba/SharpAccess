using SharpAccess.Persistence;

namespace SharpAccess.Postgres;

internal static class PostgresAuthProviderFactory
{
    // Creates PostgreSQL provider infrastructure from a provider-owned connection string.
    internal static PostgresAuthProviderComponents Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        PostgresAuthOptions options = new()
        {
            ConnectionString = connectionString
        };
        return CreateWithConnections(new PostgresAuthConnectionFactory(options));
    }

    // Creates PostgreSQL provider infrastructure from the selected logical-connection factory.
    internal static PostgresAuthProviderComponents CreateWithConnections(IPostgresAuthConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        PostgresAuthCommandFactory commands = new();
        PostgresAuthSqlDialect dialect = new();
        PostgresAuthMigrationProvider migrations = new();
        PostgresAuthTransactionManager transactions = new();
        IAuthConnectionFactory neutralConnections = connections as IAuthConnectionFactory
            ?? throw new InvalidOperationException(
                "The PostgreSQL connection factory must implement the provider-neutral contract.");
        PostgresAuthDatabaseProvider databaseProvider = new(
            neutralConnections,
            commands,
            dialect,
            migrations,
            transactions);
        PostgresAuthSchemaManager schemaManager = new(connections, migrations);
        PostgresAuthorizationStore authorizationStore = new(connections);
        return new PostgresAuthProviderComponents(
            connections,
            commands,
            dialect,
            migrations,
            transactions,
            databaseProvider,
            schemaManager,
            authorizationStore);
    }
}
