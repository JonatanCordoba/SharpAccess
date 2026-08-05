namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthorizationStore
{
    // Gets global permissions without consulting an active tenant.
    internal Task<IReadOnlyList<string>> GetGlobalPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        ReadStringsAsync(GlobalPermissionsSql, userId, tenantId: null, cancellationToken);

    // Gets permissions assigned only in the selected active tenant.
    internal Task<IReadOnlyList<string>> GetTenantPermissionsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ReadStringsAsync(TenantPermissionsSql, userId, tenantId, cancellationToken);
}
