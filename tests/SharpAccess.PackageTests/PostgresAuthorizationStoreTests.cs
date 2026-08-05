using System.Reflection;

namespace SharpAccess.PackageTests;

public sealed class PostgresAuthorizationStoreTests
{
    // Verifies that the PostgreSQL authorization store slice remains internal.
    [Fact]
    public void AuthorizationStoreSliceRemainsInternal()
    {
        Assembly assembly = Assembly.Load("SharpAccess.Postgres");
        Type store = assembly.GetType("SharpAccess.Postgres.PostgresAuthorizationStore", throwOnError: true)!;
        Assert.False(store.IsPublic);
    }

    // Verifies that global and tenant authorization SQL use separate catalogs and deterministic ordering.
    [Fact]
    public void EffectiveAuthorizationSqlSeparatesScopesAndUsesStableOrdering()
    {
        Assembly assembly = Assembly.Load("SharpAccess.Postgres");
        Type store = assembly.GetType("SharpAccess.Postgres.PostgresAuthorizationStore", throwOnError: true)!;

        string globalRoles = ReadConstant(store, "GlobalRolesSql");
        string globalPermissions = ReadConstant(store, "GlobalPermissionsSql");
        string tenantRoles = ReadConstant(store, "TenantRolesSql");
        string tenantPermissions = ReadConstant(store, "TenantPermissionsSql");

        Assert.Contains("auth_global_roles", globalRoles, StringComparison.Ordinal);
        Assert.Contains("auth_global_user_roles", globalRoles, StringComparison.Ordinal);
        Assert.Contains("ORDER BY r.name", globalRoles, StringComparison.Ordinal);
        Assert.DoesNotContain("auth_tenant_", globalRoles, StringComparison.Ordinal);

        Assert.Contains("auth_global_permissions", globalPermissions, StringComparison.Ordinal);
        Assert.Contains("ORDER BY p.name", globalPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("auth_tenant_", globalPermissions, StringComparison.Ordinal);

        Assert.Contains("auth_tenant_roles", tenantRoles, StringComparison.Ordinal);
        Assert.Contains("ur.tenant_id=@tenantId", tenantRoles, StringComparison.Ordinal);
        Assert.Contains("ORDER BY r.name", tenantRoles, StringComparison.Ordinal);
        Assert.DoesNotContain("auth_global_", tenantRoles, StringComparison.Ordinal);

        Assert.Contains("auth_tenant_permissions", tenantPermissions, StringComparison.Ordinal);
        Assert.Contains("ur.tenant_id=@tenantId", tenantPermissions, StringComparison.Ordinal);
        Assert.Contains("ORDER BY p.name", tenantPermissions, StringComparison.Ordinal);
        Assert.DoesNotContain("auth_global_", tenantPermissions, StringComparison.Ordinal);
    }

    // Reads one internal constant value without exposing the implementation type publicly.
    private static string ReadConstant(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)field.GetRawConstantValue()!;
    }
}
