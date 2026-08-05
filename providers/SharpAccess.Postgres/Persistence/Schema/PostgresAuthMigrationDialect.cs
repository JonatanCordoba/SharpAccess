using System.Globalization;
using System.Text;
using SharpAccess.Persistence;

namespace SharpAccess.Postgres;

// Supplies PostgreSQL-owned migration SQL to the provider-neutral migration engine.
internal sealed class PostgresAuthMigrationDialect : IAuthMigrationDialect
{
    internal const long MigrationLockKey = 7_342_727_002_001;
    private const string ExternalScriptLockTimeout = "30s";

    public string ProviderName => "postgres";
    public bool UsesTransactionalDdl => true;
    public string MigrationLedgerExistsSql => "SELECT CASE WHEN to_regclass('auth_schema_migrations') IS NULL THEN 0 ELSE 1 END;";
    public string ChecksumLedgerExistsSql => "SELECT CASE WHEN to_regclass('auth_schema_migration_checksums') IS NULL THEN 0 ELSE 1 END;";
    public string EnsureMigrationLedgerSql => "CREATE TABLE IF NOT EXISTS auth_schema_migrations(id text PRIMARY KEY,applied_utc timestamptz NOT NULL);";
    public string EnsureChecksumLedgerSql => "CREATE TABLE IF NOT EXISTS auth_schema_migration_checksums(id text PRIMARY KEY REFERENCES auth_schema_migrations(id) ON DELETE CASCADE,checksum char(64) NOT NULL CHECK(length(checksum)=64));";
    public string ReadAppliedMigrationsSql => "SELECT id FROM auth_schema_migrations ORDER BY id;";
    public string ReadChecksumsSql => "SELECT id,checksum FROM auth_schema_migration_checksums ORDER BY id;";
    public string InsertAppliedMigrationSql => "INSERT INTO auth_schema_migrations(id,applied_utc) VALUES(@id,@appliedUtc);";
    public string InsertChecksumSql => "INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES(@id,@checksum);";
    public string InsertChecksumIfMissingSql => "INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES(@id,@checksum) ON CONFLICT(id) DO NOTHING;";
    public string? AcquireMigrationLockSql => FormattableString.Invariant($"SELECT pg_try_advisory_xact_lock({MigrationLockKey});");
    public string? ReleaseMigrationLockSql => null;

    // Converts a PostgreSQL metadata scalar into a table-existence result.
    public bool IsTablePresent(object? value) => value is true || Convert.ToInt32(value, CultureInfo.InvariantCulture) > 0;

    // Confirms successful PostgreSQL advisory-lock acquisition.
    public bool IsMigrationLockAcquired(object? value) => value is true;

    // Keeps provider-native UTC timestamps as DateTimeOffset values.
    public object FormatAppliedUtc(DateTimeOffset value) => value.ToUniversalTime();

    // Builds one bounded transactional PostgreSQL migration script for the observed schema state.
    public string BuildScript(SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations)
    {
        StringBuilder script = new();
        script.AppendLine("BEGIN;");
        script.Append("SET LOCAL lock_timeout = '").Append(ExternalScriptLockTimeout).AppendLine("';");
        script.Append("SELECT pg_advisory_xact_lock(").Append(MigrationLockKey.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        script.AppendLine(EnsureMigrationLedgerSql);
        script.AppendLine(EnsureChecksumLedgerSql);
        AppendStatements(script, status, migrations);
        script.AppendLine("COMMIT;");
        return script.ToString();
    }

    // Appends checksum baselines and pending PostgreSQL migrations.
    private static void AppendStatements(StringBuilder script, SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations)
    {
        HashSet<string> missing = status.MissingChecksums.ToHashSet(StringComparer.Ordinal);
        HashSet<string> pending = status.PendingMigrations.ToHashSet(StringComparer.Ordinal);
        foreach (AuthMigration migration in migrations)
        {
            string id = AuthMigrationSupport.EscapeSqlLiteral(migration.Id);
            if (missing.Contains(migration.Id))
            {
                script.Append("INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES('").Append(id).Append("','")
                    .Append(migration.Checksum).AppendLine("') ON CONFLICT(id) DO NOTHING;");
            }

            if (pending.Contains(migration.Id))
            {
                script.AppendLine(migration.Sql.Trim());
                script.Append("INSERT INTO auth_schema_migrations(id,applied_utc) VALUES('").Append(id).AppendLine("',CURRENT_TIMESTAMP);");
                script.Append("INSERT INTO auth_schema_migration_checksums(id,checksum) VALUES('").Append(id).Append("','")
                    .Append(migration.Checksum).AppendLine("');");
            }
        }
    }
}
