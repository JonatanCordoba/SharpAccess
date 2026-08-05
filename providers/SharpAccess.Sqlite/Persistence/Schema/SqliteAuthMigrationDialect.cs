using System.Globalization;
using System.Text;
using SharpAccess.Persistence;

namespace SharpAccess.Sqlite;

// Supplies SQLite-owned migration SQL to the provider-neutral migration engine.
internal sealed class SqliteAuthMigrationDialect : IAuthMigrationDialect
{
    public string ProviderName => "sqlite";
    public bool UsesTransactionalDdl => true;
    public string MigrationLedgerExistsSql => "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='auth_schema_migrations';";
    public string ChecksumLedgerExistsSql => "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='auth_schema_migration_checksums';";
    public string EnsureMigrationLedgerSql => "CREATE TABLE IF NOT EXISTS auth_schema_migrations(id TEXT PRIMARY KEY,applied_utc TEXT NOT NULL);";
    public string EnsureChecksumLedgerSql => "CREATE TABLE IF NOT EXISTS auth_schema_migration_checksums(id TEXT PRIMARY KEY,checksum TEXT NOT NULL CHECK(length(checksum)=64),FOREIGN KEY(id) REFERENCES auth_schema_migrations(id) ON DELETE CASCADE);";
    public string ReadAppliedMigrationsSql => "SELECT id FROM auth_schema_migrations ORDER BY id;";
    public string ReadChecksumsSql => "SELECT id,checksum FROM auth_schema_migration_checksums ORDER BY id;";
    public string InsertAppliedMigrationSql => "INSERT INTO auth_schema_migrations(id,applied_utc) VALUES(@id,@appliedUtc);";
    public string InsertChecksumSql => "INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES(@id,@checksum);";
    public string InsertChecksumIfMissingSql => "INSERT OR IGNORE INTO auth_schema_migration_checksums(id,checksum) VALUES(@id,@checksum);";
    public string? AcquireMigrationLockSql => null;
    public string? ReleaseMigrationLockSql => null;

    // Converts a SQLite count scalar into a table-existence result.
    public bool IsTablePresent(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture) > 0;

    // SQLite obtains migration exclusion through its serializable write transaction.
    public bool IsMigrationLockAcquired(object? value) => true;

    // Formats migration timestamps as invariant UTC text.
    public object FormatAppliedUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // Builds one transactional SQLite migration script for the observed schema state.
    public string BuildScript(SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations)
    {
        StringBuilder script = new();
        script.AppendLine("PRAGMA foreign_keys = ON;");
        script.AppendLine("BEGIN IMMEDIATE;");
        script.AppendLine(EnsureMigrationLedgerSql);
        script.AppendLine(EnsureChecksumLedgerSql);
        AppendChecksumBaselines(script, status, migrations);
        AppendPendingMigrations(script, status, migrations);
        script.AppendLine("COMMIT;");
        return script.ToString();
    }

    // Appends immutable checksum baselines for previously applied migrations.
    private static void AppendChecksumBaselines(StringBuilder script, SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations)
    {
        HashSet<string> missing = status.MissingChecksums.ToHashSet(StringComparer.Ordinal);
        foreach (AuthMigration migration in migrations.Where(migration => missing.Contains(migration.Id)))
        {
            script.Append("INSERT OR IGNORE INTO auth_schema_migration_checksums(id,checksum) VALUES('")
                .Append(AuthMigrationSupport.EscapeSqlLiteral(migration.Id)).Append("','")
                .Append(migration.Checksum).AppendLine("');");
        }
    }

    // Appends pending SQLite DDL and ledger records in provider order.
    private static void AppendPendingMigrations(StringBuilder script, SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations)
    {
        HashSet<string> pending = status.PendingMigrations.ToHashSet(StringComparer.Ordinal);
        foreach (AuthMigration migration in migrations.Where(migration => pending.Contains(migration.Id)))
        {
            script.AppendLine(migration.Sql.Trim());
            script.Append("INSERT INTO auth_schema_migrations(id,applied_utc) VALUES('")
                .Append(AuthMigrationSupport.EscapeSqlLiteral(migration.Id))
                .AppendLine("',strftime('%Y-%m-%dT%H:%M:%fZ','now'));");
            script.Append("INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES('")
                .Append(AuthMigrationSupport.EscapeSqlLiteral(migration.Id)).Append("','")
                .Append(migration.Checksum).AppendLine("');");
        }
    }
}
