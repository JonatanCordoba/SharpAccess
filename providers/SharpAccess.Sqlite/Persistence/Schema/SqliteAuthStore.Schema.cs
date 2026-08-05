using System.Data.Common;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    private static readonly SqliteAuthMigrationProvider MigrationProvider = new();

    // Preserves startup initialization by applying provider-owned migrations.
    public Task InitializeAsync(CancellationToken cancellationToken = default) => MigrateAsync(cancellationToken);

    // Applies all pending SQLite migrations through the shared migration engine.
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().MigrateAsync(cancellationToken);

    // Validates SQLite migration history without issuing DDL or DML.
    public Task ValidateAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().ValidateAsync(cancellationToken);

    // Reads provider-neutral SQLite schema status without mutation.
    public Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().GetStatusAsync(cancellationToken);

    // Generates a transactional SQLite migration script for the current database state.
    public Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default) =>
        CreateMigrationManager().GenerateScriptAsync(cancellationToken);

    // Creates one migration manager using provider-owned SQL and logical connection creation.
    private AuthMigrationManager CreateMigrationManager() => new(
        OpenMigrationConnectionAsync,
        MigrationProvider,
        new SqliteAuthMigrationDialect());

    // Opens one initialized SQLite connection for a migration operation.
    private async ValueTask<DbConnection> OpenMigrationConnectionAsync(CancellationToken cancellationToken) =>
        await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
}
