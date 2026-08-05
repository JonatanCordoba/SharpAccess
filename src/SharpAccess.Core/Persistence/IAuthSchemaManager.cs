namespace SharpAccess.Persistence;

internal interface IAuthSchemaManager
{
    // Preserves the historical initialization contract by applying provider-owned migrations.
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // Applies all pending provider-owned migrations.
    Task MigrateAsync(CancellationToken cancellationToken = default);

    // Validates the current schema without applying DDL.
    Task ValidateAsync(CancellationToken cancellationToken = default);

    // Reads provider-neutral migration status without mutating the database.
    Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    // Generates a provider-native external migration script for the current schema state.
    Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default);
}
