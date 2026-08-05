using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Postgres.Migrations;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthMigrationProvider : IAuthMigrationProvider
{
    private static readonly AuthMigration[] Migrations =
    [
        .. PostgresMigrations.All.Select(static migration => new AuthMigration(migration.Id, migration.Sql)),
        new AuthMigration(PostgresAuthorizationScopeMigration.Id, PostgresAuthorizationScopeMigration.Sql),
        new AuthMigration(PostgresTenantOwnerRoleMigration.Id, PostgresTenantOwnerRoleMigration.Sql),
        new AuthMigration(PostgresAuthorizationDefaultCorrectionMigration.Id, PostgresAuthorizationDefaultCorrectionMigration.Sql),
        new AuthMigration(PostgresLegacyTenantRoleCleanupMigration.Id, PostgresLegacyTenantRoleCleanupMigration.Sql),
        new AuthMigration(PostgresAuthorizationReconciliationMigration.Id, PostgresAuthorizationReconciliationMigration.Sql),
        new AuthMigration(PostgresTokenHashVersionMigration.Id, PostgresTokenHashVersionMigration.Sql),
        new AuthMigration(PostgresRefreshTokenAuthenticationTimeMigration.Id, PostgresRefreshTokenAuthenticationTimeMigration.Sql),
        new AuthMigration(PostgresPaginationIndexesMigration.Id, PostgresPaginationIndexesMigration.Sql)
    ];

    // Returns PostgreSQL provider migrations in deterministic order.
    public IReadOnlyList<AuthMigration> GetMigrations() => Migrations;
}
