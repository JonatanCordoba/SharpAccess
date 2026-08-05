namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthorizationStore
{
    // Gets global roles without consulting an active tenant.
    internal Task<IReadOnlyList<string>> GetGlobalRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ReadStringsAsync(GlobalRolesSql, userId, tenantId: null, cancellationToken);

    // Gets roles assigned only in the selected active tenant.
    internal Task<IReadOnlyList<string>> GetTenantRolesAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ReadStringsAsync(TenantRolesSql, userId, tenantId, cancellationToken);
}
