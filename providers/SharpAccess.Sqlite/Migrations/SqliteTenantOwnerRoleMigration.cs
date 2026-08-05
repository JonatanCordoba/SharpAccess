using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteTenantOwnerRoleMigration
{
    internal const string Id = "006_add_immutable_tenant_owner_role";

    internal const string Sql = """
        INSERT OR IGNORE INTO auth_tenant_roles(
            tenant_id,id,name,normalized_name,description,is_system,created_utc)
        SELECT id,'40000000-0000-0000-0000-000000000001','Owner','OWNER',
               'Immutable tenant owner role.',1,created_utc
        FROM auth_tenants;

        INSERT OR IGNORE INTO auth_tenant_role_permissions(
            tenant_id,role_id,permission_id,created_utc)
        SELECT t.id,'40000000-0000-0000-0000-000000000001',p.id,t.created_utc
        FROM auth_tenants t
        INNER JOIN auth_tenant_permissions p ON p.tenant_id=t.id;

        INSERT OR IGNORE INTO auth_tenant_user_roles(
            id,tenant_id,user_id,role_id,created_utc)
        SELECT lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' ||
               substr(lower(hex(randomblob(2))),2) || '-' ||
               substr('89ab',abs(random()) % 4 + 1,1) ||
               substr(lower(hex(randomblob(2))),2) || '-' || lower(hex(randomblob(6))),
               o.tenant_id,o.user_id,
               '40000000-0000-0000-0000-000000000001',o.assigned_utc
        FROM auth_tenant_owners o;
        """;
}
