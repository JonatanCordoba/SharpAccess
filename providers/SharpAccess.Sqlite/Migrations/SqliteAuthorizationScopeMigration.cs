using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteAuthorizationScopeMigration
{
    internal const string Id = "005_split_global_tenant_authorization";

    internal const string Sql = """
        CREATE TABLE auth_global_roles(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            is_system INTEGER NOT NULL CHECK(is_system IN (0,1)),
            created_utc TEXT NOT NULL
        );

        CREATE TABLE auth_global_permissions(
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            description TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );

        CREATE TABLE auth_global_role_permissions(
            role_id TEXT NOT NULL,
            permission_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            PRIMARY KEY(role_id,permission_id),
            FOREIGN KEY(role_id) REFERENCES auth_global_roles(id) ON DELETE CASCADE,
            FOREIGN KEY(permission_id) REFERENCES auth_global_permissions(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_global_user_roles(
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            role_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            UNIQUE(user_id,role_id),
            FOREIGN KEY(user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
            FOREIGN KEY(role_id) REFERENCES auth_global_roles(id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_global_user_roles_user ON auth_global_user_roles(user_id);

        CREATE TABLE auth_tenant_roles(
            tenant_id TEXT NOT NULL,
            id TEXT NOT NULL,
            name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            description TEXT NOT NULL,
            is_system INTEGER NOT NULL CHECK(is_system IN (0,1)),
            created_utc TEXT NOT NULL,
            PRIMARY KEY(tenant_id,id),
            UNIQUE(tenant_id,normalized_name),
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_tenant_permissions(
            tenant_id TEXT NOT NULL,
            id TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            PRIMARY KEY(tenant_id,id),
            UNIQUE(tenant_id,name),
            FOREIGN KEY(tenant_id) REFERENCES auth_tenants(id) ON DELETE CASCADE
        );

        CREATE TABLE auth_tenant_role_permissions(
            tenant_id TEXT NOT NULL,
            role_id TEXT NOT NULL,
            permission_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            PRIMARY KEY(tenant_id,role_id,permission_id),
            FOREIGN KEY(tenant_id,role_id) REFERENCES auth_tenant_roles(tenant_id,id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id,permission_id) REFERENCES auth_tenant_permissions(tenant_id,id) ON DELETE CASCADE
        );

        CREATE TABLE auth_tenant_user_roles(
            id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            user_id TEXT NOT NULL,
            role_id TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            UNIQUE(tenant_id,user_id,role_id),
            FOREIGN KEY(tenant_id,user_id) REFERENCES auth_tenant_memberships(tenant_id,user_id) ON DELETE CASCADE,
            FOREIGN KEY(tenant_id,role_id) REFERENCES auth_tenant_roles(tenant_id,id) ON DELETE CASCADE
        );
        CREATE INDEX ix_auth_tenant_user_roles_user ON auth_tenant_user_roles(user_id,tenant_id);

        CREATE TABLE auth_tenant_owners(
            tenant_id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL,
            assigned_utc TEXT NOT NULL,
            FOREIGN KEY(tenant_id,user_id) REFERENCES auth_tenant_memberships(tenant_id,user_id) ON DELETE RESTRICT
        );
        CREATE INDEX ix_auth_tenant_owners_user ON auth_tenant_owners(user_id);

        INSERT INTO auth_global_roles
            SELECT * FROM auth_roles;
        INSERT INTO auth_global_permissions
            SELECT * FROM auth_permissions;
        INSERT INTO auth_global_role_permissions
            SELECT * FROM auth_role_permissions;
        INSERT INTO auth_global_user_roles(id,user_id,role_id,created_utc)
            SELECT id,user_id,role_id,created_utc
            FROM auth_user_roles
            WHERE tenant_id IS NULL;

        INSERT OR IGNORE INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc)
            SELECT DISTINCT ur.tenant_id,r.id,r.name,r.normalized_name,r.description,r.is_system,r.created_utc
            FROM auth_user_roles ur
            INNER JOIN auth_roles r ON r.id=ur.role_id
            WHERE ur.tenant_id IS NOT NULL;

        INSERT OR IGNORE INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc)
            SELECT tr.tenant_id,p.id,p.name,p.description,p.created_utc
            FROM auth_tenant_roles tr
            CROSS JOIN auth_permissions p;

        INSERT OR IGNORE INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc)
            SELECT tr.tenant_id,rp.role_id,rp.permission_id,rp.created_utc
            FROM auth_tenant_roles tr
            INNER JOIN auth_role_permissions rp ON rp.role_id=tr.id;

        INSERT OR IGNORE INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc)
            SELECT id,'30000000-0000-0000-0000-000000000001','tenant.read','Read the active tenant.',created_utc FROM auth_tenants
            UNION ALL
            SELECT id,'30000000-0000-0000-0000-000000000002','tenant.members.read','Read active-tenant members.',created_utc FROM auth_tenants
            UNION ALL
            SELECT id,'30000000-0000-0000-0000-000000000003','tenant.members.manage','Manage active-tenant members.',created_utc FROM auth_tenants
            UNION ALL
            SELECT id,'30000000-0000-0000-0000-000000000004','tenant.roles.manage','Manage active-tenant role assignments.',created_utc FROM auth_tenants
            UNION ALL
            SELECT id,'30000000-0000-0000-0000-000000000005','tenant.owner.transfer','Transfer active-tenant ownership.',created_utc FROM auth_tenants;

        INSERT OR IGNORE INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc)
            SELECT id,'40000000-0000-0000-0000-000000000002','Manager','MANAGER','Manage members and tenant role assignments.',1,created_utc FROM auth_tenants
            UNION ALL
            SELECT id,'40000000-0000-0000-0000-000000000003','Member','MEMBER','Standard tenant member access.',1,created_utc FROM auth_tenants;

        INSERT OR IGNORE INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc)
            SELECT id,'40000000-0000-0000-0000-000000000002',permission_id,created_utc
            FROM auth_tenants
            CROSS JOIN (
                SELECT '30000000-0000-0000-0000-000000000001' AS permission_id
                UNION ALL SELECT '30000000-0000-0000-0000-000000000002'
                UNION ALL SELECT '30000000-0000-0000-0000-000000000003'
                UNION ALL SELECT '30000000-0000-0000-0000-000000000004'
            )
            UNION ALL
            SELECT id,'40000000-0000-0000-0000-000000000003',permission_id,created_utc
            FROM auth_tenants
            CROSS JOIN (
                SELECT '30000000-0000-0000-0000-000000000001' AS permission_id
                UNION ALL SELECT '30000000-0000-0000-0000-000000000002'
            );

        INSERT OR IGNORE INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
            SELECT id,tenant_id,user_id,
                CASE role_id
                    WHEN '10000000-0000-0000-0000-000000000001' THEN '40000000-0000-0000-0000-000000000002'
                    WHEN '10000000-0000-0000-0000-000000000002' THEN '40000000-0000-0000-0000-000000000003'
                    WHEN '10000000-0000-0000-0000-000000000003' THEN '40000000-0000-0000-0000-000000000002'
                    ELSE role_id
                END,
                created_utc
            FROM auth_user_roles
            WHERE tenant_id IS NOT NULL;

        INSERT INTO auth_tenant_owners(tenant_id,user_id,assigned_utc)
            SELECT t.id,
                COALESCE(
                    (SELECT a.user_id
                     FROM auth_security_audit_logs a
                     WHERE a.tenant_id=t.id
                       AND a.event_type='tenant_created'
                       AND a.user_id IS NOT NULL
                       AND EXISTS(
                           SELECT 1 FROM auth_tenant_memberships m
                           WHERE m.tenant_id=t.id AND m.user_id=a.user_id)
                     ORDER BY a.created_utc,a.id
                     LIMIT 1),
                    (SELECT m.user_id
                     FROM auth_tenant_memberships m
                     WHERE m.tenant_id=t.id
                     ORDER BY m.created_utc,m.user_id
                     LIMIT 1)),
                COALESCE(
                    (SELECT a.created_utc
                     FROM auth_security_audit_logs a
                     WHERE a.tenant_id=t.id AND a.event_type='tenant_created' AND a.user_id IS NOT NULL
                     ORDER BY a.created_utc,a.id
                     LIMIT 1),
                    t.created_utc)
            FROM auth_tenants t
            WHERE EXISTS(SELECT 1 FROM auth_tenant_memberships m WHERE m.tenant_id=t.id);

        DROP TABLE auth_role_permissions;
        DROP TABLE auth_user_roles;
        DROP TABLE auth_permissions;
        DROP TABLE auth_roles;

        UPDATE auth_users
        SET security_version=security_version+1,
            updated_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now');
        UPDATE auth_refresh_tokens
        SET revoked_utc=strftime('%Y-%m-%dT%H:%M:%fZ','now')
        WHERE revoked_utc IS NULL;
        """;
}
