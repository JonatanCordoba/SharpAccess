using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Postgres.Migrations;

[ExcludeFromCodeCoverage]
internal static class PostgresAuthorizationDefaultCorrectionMigration
{
    internal const string Id = "007_remove_standard_user_cross_tenant_read";

    internal const string Sql = """
        DELETE FROM auth_global_role_permissions
        WHERE role_id='10000000-0000-0000-0000-000000000002'::uuid
          AND permission_id='20000000-0000-0000-0000-000000000009'::uuid;
        """;
}
