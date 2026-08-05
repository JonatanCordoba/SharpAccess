using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

[ExcludeFromCodeCoverage]
internal static class PostgresTenantOwnerRoleMigration
{
    internal const string Id = "006_add_immutable_tenant_owner_role";

    internal const string Sql = """
        INSERT INTO auth_tenant_roles(
            tenant_id,id,name,normalized_name,description,is_system,created_utc)
        SELECT id,'40000000-0000-0000-0000-000000000001'::uuid,'Owner','OWNER',
               'Immutable tenant owner role.',true,created_utc
        FROM auth_tenants
        ON CONFLICT DO NOTHING;

        INSERT INTO auth_tenant_role_permissions(
            tenant_id,role_id,permission_id,created_utc)
        SELECT t.id,'40000000-0000-0000-0000-000000000001'::uuid,p.id,t.created_utc
        FROM auth_tenants t
        INNER JOIN auth_tenant_permissions p ON p.tenant_id=t.id
        ON CONFLICT DO NOTHING;

        INSERT INTO auth_tenant_user_roles(
            id,tenant_id,user_id,role_id,created_utc)
        SELECT (
            substr(owner_digest.value,1,8) || '-' ||
            substr(owner_digest.value,9,4) || '-4' ||
            substr(owner_digest.value,14,3) || '-8' ||
            substr(owner_digest.value,18,3) || '-' ||
            substr(owner_digest.value,21,12)
        )::uuid,
        o.tenant_id,o.user_id,'40000000-0000-0000-0000-000000000001'::uuid,o.assigned_utc
        FROM auth_tenant_owners o
        CROSS JOIN LATERAL (
            SELECT md5(o.tenant_id::text || o.user_id::text || 'owner') AS value -- DevSkim: ignore DS126858
        ) owner_digest
        ON CONFLICT DO NOTHING;
        """;
}
