using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

[ExcludeFromCodeCoverage]
internal static class PostgresPaginationIndexesMigration
{
    internal const string Id = "012_pagination_indexes";
    internal const string Sql = """
        DROP INDEX IF EXISTS ix_auth_audit_created;
        CREATE INDEX ix_auth_audit_created
            ON auth_security_audit_logs(created_utc DESC,id ASC);
        CREATE INDEX IF NOT EXISTS ix_auth_global_roles_page
            ON auth_global_roles(created_utc DESC,id ASC);
        CREATE INDEX IF NOT EXISTS ix_auth_global_permissions_page
            ON auth_global_permissions(created_utc DESC,id ASC);
        CREATE INDEX IF NOT EXISTS ix_auth_tenant_memberships_user_page
            ON auth_tenant_memberships(user_id ASC,created_utc DESC,tenant_id ASC);
        CREATE INDEX IF NOT EXISTS ix_auth_tenant_memberships_tenant_page
            ON auth_tenant_memberships(tenant_id ASC,created_utc DESC,user_id ASC);
        CREATE INDEX IF NOT EXISTS ix_auth_users_created
            ON auth_users(created_utc DESC,id ASC);
        """;
}
