using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteAuthorizationDefaultCorrectionMigration
{
    internal const string Id = "007_remove_standard_user_cross_tenant_read";

    internal const string Sql = """
        DELETE FROM auth_global_role_permissions
        WHERE role_id='10000000-0000-0000-0000-000000000002'
          AND permission_id='20000000-0000-0000-0000-000000000009';
        """;
}
