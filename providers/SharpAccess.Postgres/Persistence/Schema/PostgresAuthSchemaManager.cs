using System.Data.Common;
using SharpAccess.Persistence;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthSchemaManager(
    IPostgresAuthConnectionFactory connections,
    IAuthMigrationProvider migrations) : IAuthSchemaManager
{
    // Preserves startup initialization by applying provider-owned migrations.
    public Task InitializeAsync(CancellationToken cancellationToken = default) => MigrateAsync(cancellationToken);

    // Applies all pending PostgreSQL migrations.
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().MigrateAsync(cancellationToken);

    // Validates PostgreSQL migration history without mutation.
    public Task ValidateAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().ValidateAsync(cancellationToken);

    // Reads provider-neutral PostgreSQL schema status.
    public Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().GetStatusAsync(cancellationToken);

    // Generates a transactional PostgreSQL migration script.
    public Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().GenerateScriptAsync(cancellationToken);

    // Creates one migration manager using provider-owned SQL.
    private AuthMigrationManager CreateMigrationManager() => new(
        OpenMigrationConnectionAsync,
        migrations,
        new PostgresAuthMigrationDialect());

    // Opens one initialized PostgreSQL connection for a migration operation.
    private async ValueTask<DbConnection> OpenMigrationConnectionAsync(CancellationToken cancellationToken) =>
        await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
}
