using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteLegacyTenantRoleCleanupMigration
{
    internal const string Id = "008_remove_legacy_global_roles_from_tenant_catalogs";

    internal const string Sql = """
        DELETE FROM auth_tenant_role_permissions
        WHERE role_id IN (
            '10000000-0000-0000-0000-000000000001',
            '10000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000003');

        DELETE FROM auth_tenant_user_roles
        WHERE role_id IN (
            '10000000-0000-0000-0000-000000000001',
            '10000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000003');

        DELETE FROM auth_tenant_roles
        WHERE id IN (
            '10000000-0000-0000-0000-000000000001',
            '10000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000003');
        """;
}
