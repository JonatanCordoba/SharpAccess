using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

[ExcludeFromCodeCoverage]
internal static class PostgresAuthorizationScopeMigration
{
    internal const string Id = "005_split_global_tenant_authorization";

    internal const string Sql = """
        CREATE TABLE auth_global_roles(
            id uuid PRIMARY KEY,
            name text NOT NULL,
            normalized_name text NOT NULL UNIQUE,
            description text NOT NULL,
            is_system boolean NOT NULL,
            created_utc timestamptz NOT NULL
        );
        CREATE TABLE auth_global_permissions(
            id uuid PRIMARY KEY,
            name text NOT NULL UNIQUE,
            description text NOT NULL,
            created_utc timestamptz NOT NULL
        );
        CREATE TABLE auth_global_role_permissions(
            role_id uuid NOT NULL REFERENCES auth_global_roles(id) ON DELETE CASCADE,
            permission_id uuid NOT NULL REFERENCES auth_global_permissions(id) ON DELETE CASCADE,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(role_id,permission_id)
        );
        CREATE TABLE auth_global_user_roles(
            id uuid PRIMARY KEY,
            user_id uuid NOT NULL REFERENCES auth_users(id) ON DELETE CASCADE,
            role_id uuid NOT NULL REFERENCES auth_global_roles(id) ON DELETE CASCADE,
            created_utc timestamptz NOT NULL,
            UNIQUE(user_id,role_id)
        );
        CREATE INDEX ix_auth_global_user_roles_user ON auth_global_user_roles(user_id);

        CREATE TABLE auth_tenant_roles(
            tenant_id uuid NOT NULL REFERENCES auth_tenants(id) ON DELETE CASCADE,
            id uuid NOT NULL,
            name text NOT NULL,
            normalized_name text NOT NULL,
            description text NOT NULL,
            is_system boolean NOT NULL,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(tenant_id,id),
            UNIQUE(tenant_id,normalized_name)
        );
        CREATE TABLE auth_tenant_permissions(
            tenant_id uuid NOT NULL REFERENCES auth_tenants(id) ON DELETE CASCADE,
            id uuid NOT NULL,
            name text NOT NULL,
            description text NOT NULL,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(tenant_id,id),
            UNIQUE(tenant_id,name)
        );
        CREATE TABLE auth_tenant_role_permissions(
            tenant_id uuid NOT NULL,
            role_id uuid NOT NULL,
            permission_id uuid NOT NULL,
            created_utc timestamptz NOT NULL,
            PRIMARY KEY(tenant_id,role_id,permission_id),
            FOREIGN KEY(tenant_id,role_id) REFERENCES auth_tenant_roles(tenant_id,id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id,permission_id) REFERENCES auth_tenant_permissions(tenant_id,id) ON DELETE CASCADE
        );
        CREATE TABLE auth_tenant_user_roles(
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            user_id uuid NOT NULL,
            role_id uuid NOT NULL,
            created_utc timestamptz NOT NULL,
            UNIQUE(tenant_id,user_id,role_id),
            FOREIGN KEY(tenant_id,user_id) REFERENCES auth_tenant_memberships(tenant_id,user_id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id,role_id) REFERENCES auth_tenant_roles(tenant_id,id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_tenant_user_roles_user ON auth_tenant_user_roles(user_id,tenant_id);
        CREATE TABLE auth_tenant_owners(
            tenant_id uuid PRIMARY KEY REFERENCES auth_tenants(id) ON DELETE CASCADE,
            user_id uuid NOT NULL,
            assigned_utc timestamptz NOT NULL,
            FOREIGN KEY(tenant_id,user_id) REFERENCES auth_tenant_memberships(tenant_id,user_id) ON DELETE RESTRICT
        );
        CREATE INDEX ix_auth_tenant_owners_user ON auth_tenant_owners(user_id);

        INSERT INTO auth_global_roles SELECT * FROM auth_roles;
        INSERT INTO auth_global_permissions SELECT * FROM auth_permissions;
        INSERT INTO auth_global_role_permissions SELECT * FROM auth_role_permissions;
        INSERT INTO auth_global_user_roles(id,user_id,role_id,created_utc)
            SELECT id,user_id,role_id,created_utc FROM auth_user_roles WHERE tenant_id IS NULL;

        INSERT INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc)
            SELECT DISTINCT ur.tenant_id,r.id,r.name,r.normalized_name,r.description,r.is_system,r.created_utc
            FROM auth_user_roles ur
            INNER JOIN auth_roles r ON r.id=ur.role_id
            WHERE ur.tenant_id IS NOT NULL
            ON CONFLICT DO NOTHING;
        INSERT INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc)
            SELECT tr.tenant_id,p.id,p.name,p.description,p.created_utc
            FROM auth_tenant_roles tr CROSS JOIN auth_permissions p
            ON CONFLICT DO NOTHING;
        INSERT INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc)
            SELECT tr.tenant_id,rp.role_id,rp.permission_id,rp.created_utc
            FROM auth_tenant_roles tr
            INNER JOIN auth_role_permissions rp ON rp.role_id=tr.id
            ON CONFLICT DO NOTHING;

        INSERT INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc)
            SELECT id,'30000000-0000-0000-0000-000000000001'::uuid,'tenant.read','Read the active tenant.',created_utc FROM auth_tenants
            UNION ALL SELECT id,'30000000-0000-0000-0000-000000000002'::uuid,'tenant.members.read','Read active-tenant members.',created_utc FROM auth_tenants
            UNION ALL SELECT id,'30000000-0000-0000-0000-000000000003'::uuid,'tenant.members.manage','Manage active-tenant members.',created_utc FROM auth_tenants
            UNION ALL SELECT id,'30000000-0000-0000-0000-000000000004'::uuid,'tenant.roles.manage','Manage active-tenant role assignments.',created_utc FROM auth_tenants
            UNION ALL SELECT id,'30000000-0000-0000-0000-000000000005'::uuid,'tenant.owner.transfer','Transfer active-tenant ownership.',created_utc FROM auth_tenants
            ON CONFLICT DO NOTHING;
        INSERT INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc)
            SELECT id,'40000000-0000-0000-0000-000000000002'::uuid,'Manager','MANAGER','Manage members and tenant role assignments.',true,created_utc FROM auth_tenants
            UNION ALL SELECT id,'40000000-0000-0000-0000-000000000003'::uuid,'Member','MEMBER','Standard tenant member access.',true,created_utc FROM auth_tenants
            ON CONFLICT DO NOTHING;
        INSERT INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc)
            SELECT id,'40000000-0000-0000-0000-000000000002'::uuid,permission_id,created_utc
            FROM auth_tenants CROSS JOIN (VALUES
                ('30000000-0000-0000-0000-000000000001'::uuid),
                ('30000000-0000-0000-0000-000000000002'::uuid),
                ('30000000-0000-0000-0000-000000000003'::uuid),
                ('30000000-0000-0000-0000-000000000004'::uuid)) AS permissions(permission_id)
            UNION ALL
            SELECT id,'40000000-0000-0000-0000-000000000003'::uuid,permission_id,created_utc
            FROM auth_tenants CROSS JOIN (VALUES
                ('30000000-0000-0000-0000-000000000001'::uuid),
                ('30000000-0000-0000-0000-000000000002'::uuid)) AS permissions(permission_id)
            ON CONFLICT DO NOTHING;
        INSERT INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
            SELECT id,tenant_id,user_id,
                CASE role_id
                    WHEN '10000000-0000-0000-0000-000000000001'::uuid THEN '40000000-0000-0000-0000-000000000002'::uuid
                    WHEN '10000000-0000-0000-0000-000000000002'::uuid THEN '40000000-0000-0000-0000-000000000003'::uuid
                    WHEN '10000000-0000-0000-0000-000000000003'::uuid THEN '40000000-0000-0000-0000-000000000002'::uuid
                    ELSE role_id
                END,
                created_utc
            FROM auth_user_roles WHERE tenant_id IS NOT NULL
            ON CONFLICT DO NOTHING;

        INSERT INTO auth_tenant_owners(tenant_id,user_id,assigned_utc)
            SELECT t.id,
                COALESCE(
                    (SELECT a.user_id FROM auth_security_audit_logs a
                     WHERE a.tenant_id=t.id AND a.event_type='tenant_created' AND a.user_id IS NOT NULL
                       AND EXISTS(SELECT 1 FROM auth_tenant_memberships m WHERE m.tenant_id=t.id AND m.user_id=a.user_id)
                     ORDER BY a.created_utc,a.id LIMIT 1),
                    (SELECT m.user_id FROM auth_tenant_memberships m
                     WHERE m.tenant_id=t.id ORDER BY m.created_utc,m.user_id LIMIT 1)),
                COALESCE(
                    (SELECT a.created_utc FROM auth_security_audit_logs a
                     WHERE a.tenant_id=t.id AND a.event_type='tenant_created' AND a.user_id IS NOT NULL
                     ORDER BY a.created_utc,a.id LIMIT 1),
                    t.created_utc)
            FROM auth_tenants t
            WHERE EXISTS(SELECT 1 FROM auth_tenant_memberships m WHERE m.tenant_id=t.id);

        DROP TABLE auth_role_permissions;
        DROP TABLE auth_user_roles;
        DROP TABLE auth_permissions;
        DROP TABLE auth_roles;

        UPDATE auth_users SET security_version=security_version+1,updated_utc=CURRENT_TIMESTAMP;
        UPDATE auth_refresh_tokens SET revoked_utc=CURRENT_TIMESTAMP WHERE revoked_utc IS NULL;
        """;
}
