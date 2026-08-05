using System.Diagnostics.CodeAnalysis;

namespace SharpAccess.Sqlite.Migrations;

[ExcludeFromCodeCoverage]
internal static class SqliteAuthorizationReconciliationMigration
{
    internal const string Id = "009_record_authorization_reconciliation";
    internal const string Sql = """
        CREATE TABLE auth_migration_reconciliation_reports(
            migration_id TEXT PRIMARY KEY,
            recorded_utc TEXT NOT NULL,
            global_assignment_count INTEGER NOT NULL CHECK(global_assignment_count >= 0),
            tenant_assignment_count INTEGER NOT NULL CHECK(tenant_assignment_count >= 0),
            tenant_owner_count INTEGER NOT NULL CHECK(tenant_owner_count >= 0),
            ambiguous_assignment_count INTEGER NOT NULL CHECK(ambiguous_assignment_count >= 0)
        );
        INSERT INTO auth_migration_reconciliation_reports(
            migration_id,recorded_utc,global_assignment_count,tenant_assignment_count,
            tenant_owner_count,ambiguous_assignment_count)
        SELECT
            '009_record_authorization_reconciliation',
            strftime('%Y-%m-%dT%H:%M:%fZ','now'),
            (SELECT COUNT(*) FROM auth_global_user_roles),
            (SELECT COUNT(*) FROM auth_tenant_user_roles),
            (SELECT COUNT(*) FROM auth_tenant_owners),
            0;
        """;
}
