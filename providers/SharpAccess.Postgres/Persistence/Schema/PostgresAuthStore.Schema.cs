using System.Data.Common;
using SharpAccess.Persistence;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Preserves startup initialization by applying provider-owned migrations.
    public Task InitializeAsync(CancellationToken cancellationToken = default) => MigrateAsync(cancellationToken);

    // Applies all pending PostgreSQL migrations through the shared migration engine.
    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        ExecuteMigrationOperationAsync(static (manager, token) => manager.MigrateAsync(token), cancellationToken);

    // Validates PostgreSQL migration history without issuing DDL or DML.
    public Task ValidateAsync(CancellationToken cancellationToken = default) =>
        ExecuteMigrationOperationAsync(static (manager, token) => manager.ValidateAsync(token), cancellationToken);

    // Reads provider-neutral PostgreSQL schema status without mutation.
    public Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        ExecuteMigrationQueryAsync(static (manager, token) => manager.GetStatusAsync(token), cancellationToken);

    // Generates a transactional PostgreSQL migration script for the current database state.
    public Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default) =>
        ExecuteMigrationQueryAsync(static (manager, token) => manager.GenerateScriptAsync(token), cancellationToken);

    // Serializes one store migration operation and releases the local lock on every path.
    private async Task ExecuteMigrationOperationAsync(
        Func<AuthMigrationManager, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await MigrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(CreateMigrationManager(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    // Serializes one value-returning store migration operation.
    private async Task<T> ExecuteMigrationQueryAsync<T>(
        Func<AuthMigrationManager, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await MigrationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(CreateMigrationManager(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    // Creates one migration manager using PostgreSQL-owned SQL.
    private AuthMigrationManager CreateMigrationManager() => new(
        OpenMigrationConnectionAsync,
        _migrations,
        new PostgresAuthMigrationDialect());

    // Opens one initialized PostgreSQL connection for a migration operation.
    private async ValueTask<DbConnection> OpenMigrationConnectionAsync(CancellationToken cancellationToken) =>
        await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
}
