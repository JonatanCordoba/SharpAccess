namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthorizationStore
{
    // Gets the SQL used to read global role names.
    internal const string GlobalRolesSql = "SELECT DISTINCT r.name FROM auth_global_roles r INNER JOIN auth_global_user_roles ur ON ur.role_id=r.id WHERE ur.user_id=@userId ORDER BY r.name;";

    // Gets the SQL used to read global permission names.
    internal const string GlobalPermissionsSql = "SELECT DISTINCT p.name FROM auth_global_permissions p INNER JOIN auth_global_role_permissions rp ON rp.permission_id=p.id INNER JOIN auth_global_user_roles ur ON ur.role_id=rp.role_id WHERE ur.user_id=@userId ORDER BY p.name;";

    // Gets the SQL used to read active-tenant role names.
    internal const string TenantRolesSql = "SELECT DISTINCT r.name FROM auth_tenant_roles r INNER JOIN auth_tenant_user_roles ur ON ur.tenant_id=r.tenant_id AND ur.role_id=r.id WHERE ur.user_id=@userId AND ur.tenant_id=@tenantId ORDER BY r.name;";

    // Gets the SQL used to read active-tenant permission names.
    internal const string TenantPermissionsSql = "SELECT DISTINCT p.name FROM auth_tenant_permissions p INNER JOIN auth_tenant_role_permissions rp ON rp.tenant_id=p.tenant_id AND rp.permission_id=p.id INNER JOIN auth_tenant_user_roles ur ON ur.tenant_id=rp.tenant_id AND ur.role_id=rp.role_id WHERE ur.user_id=@userId AND ur.tenant_id=@tenantId ORDER BY p.name;";

    // Gets the SQL used to verify active-tenant ownership.
    internal const string TenantOwnerSql = "SELECT EXISTS(SELECT 1 FROM auth_tenant_owners WHERE tenant_id=@tenantId AND user_id=@userId);";

    // Gets the SQL used to bind authorization context to the persisted security version.
    internal const string AuthorizationVersionSql = "SELECT security_version FROM auth_users WHERE id=@userId;";
}
