using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Sqlite.Migrations;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthMigrationProvider : IAuthMigrationProvider
{
    private static readonly AuthMigration[] Migrations =
    [
        .. SqliteMigrations.All.Select(static migration => new AuthMigration(migration.Id, migration.Sql)),
        new AuthMigration(SqliteAuthorizationScopeMigration.Id, SqliteAuthorizationScopeMigration.Sql),
        new AuthMigration(SqliteTenantOwnerRoleMigration.Id, SqliteTenantOwnerRoleMigration.Sql),
        new AuthMigration(SqliteAuthorizationDefaultCorrectionMigration.Id, SqliteAuthorizationDefaultCorrectionMigration.Sql),
        new AuthMigration(SqliteLegacyTenantRoleCleanupMigration.Id, SqliteLegacyTenantRoleCleanupMigration.Sql),
        new AuthMigration(SqliteAuthorizationReconciliationMigration.Id, SqliteAuthorizationReconciliationMigration.Sql),
        new AuthMigration(SqliteTokenHashVersionMigration.Id, SqliteTokenHashVersionMigration.Sql),
        new AuthMigration(
            SqliteRefreshTokenAuthenticationTimeMigration.Id,
            SqliteRefreshTokenAuthenticationTimeMigration.Sql),
        new AuthMigration(SqlitePaginationIndexesMigration.Id, SqlitePaginationIndexesMigration.Sql)
    ];

    // Returns immutable provider-owned migrations in deterministic order.
    public IReadOnlyList<AuthMigration> GetMigrations() => Migrations;
}
