using SharpAccess;
using SharpAccess.Persistence;
using SharpAccess.Sqlite;
using Microsoft.Data.Sqlite;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqliteDatabaseErrorAndMigrationDialectTests
{
    [Fact]
    public void DatabaseErrorClassifierMapsNeutralExceptionsAndValidatesInput()
    {
        SqliteAuthDatabaseErrorClassifier classifier = new();

        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
        Assert.Equal(
            AuthDatabaseErrorCategory.Timeout,
            classifier.Classify(new TimeoutException()));
        Assert.Equal(
            AuthDatabaseErrorCategory.Unknown,
            classifier.Classify(new InvalidOperationException()));
    }

    [Fact]
    public void DatabaseErrorClassifierMapsConcreteSqliteExceptions()
    {
        SqliteAuthDatabaseErrorClassifier classifier = new();

        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY);";
            create.ExecuteNonQuery();
        }

        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO items(id) VALUES(1);";
            insert.ExecuteNonQuery();
        }

        using SqliteCommand duplicate = connection.CreateCommand();
        duplicate.CommandText = "INSERT INTO items(id) VALUES(1);";
        SqliteException exception =
            Assert.Throws<SqliteException>(() => duplicate.ExecuteNonQuery());

        Assert.Equal(
            AuthDatabaseErrorCategory.UniqueConstraint,
            classifier.Classify(exception));
    }

    [Theory]
    [InlineData(19, 1555, (int)AuthDatabaseErrorCategory.UniqueConstraint)]
    [InlineData(19, 2067, (int)AuthDatabaseErrorCategory.UniqueConstraint)]
    [InlineData(19, 787, (int)AuthDatabaseErrorCategory.ForeignKeyConstraint)]
    [InlineData(5, 0, (int)AuthDatabaseErrorCategory.Timeout)]
    [InlineData(6, 0, (int)AuthDatabaseErrorCategory.Timeout)]
    [InlineData(10, 0, (int)AuthDatabaseErrorCategory.ConnectionFailure)]
    [InlineData(14, 0, (int)AuthDatabaseErrorCategory.ConnectionFailure)]
    [InlineData(8, 0, (int)AuthDatabaseErrorCategory.PermissionDenied)]
    [InlineData(23, 0, (int)AuthDatabaseErrorCategory.PermissionDenied)]
    [InlineData(1, 0, (int)AuthDatabaseErrorCategory.SchemaMismatch)]
    [InlineData(999, 0, (int)AuthDatabaseErrorCategory.Unknown)]
    public void DatabaseErrorClassifierMapsEverySupportedSqliteCode(
        int errorCode,
        int extendedErrorCode,
        int expectedCategory)
    {
        Assert.Equal(
            (AuthDatabaseErrorCategory)expectedCategory,
            SqliteAuthDatabaseErrorClassifier.ClassifyCodes(
                errorCode,
                extendedErrorCode));
    }

    [Fact]
    public void MigrationDialectCoversLockTableAndScriptBranches()
    {
        SqliteAuthMigrationDialect dialect = new();

        Assert.Null(dialect.AcquireMigrationLockSql);
        Assert.Null(dialect.ReleaseMigrationLockSql);
        Assert.False(dialect.IsTablePresent(null));
        Assert.False(dialect.IsTablePresent(0L));
        Assert.True(dialect.IsTablePresent(1L));
        Assert.True(dialect.IsMigrationLockAcquired(null));
        Assert.Equal(
            "2026-07-16T15:00:00.0000000+00:00",
            dialect.FormatAppliedUtc(
                new DateTimeOffset(
                    2026,
                    7,
                    16,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(-3))));

        AuthMigration applied = new(
            "001_applied",
            "CREATE TABLE applied_item(id INTEGER PRIMARY KEY);");
        AuthMigration pending = new(
            "002_pending",
            "CREATE TABLE pending_item(id INTEGER PRIMARY KEY);");
        SharpAccessSchemaStatus status = new(
            "sqlite",
            migrationLedgerExists: true,
            checksumLedgerExists: true,
            appliedMigrations: new[] { applied.Id },
            pendingMigrations: new[] { pending.Id },
            unknownMigrations: Array.Empty<string>(),
            missingChecksums: new[] { applied.Id },
            checksumMismatches: Array.Empty<string>());

        string script = dialect.BuildScript(
            status,
            new[] { applied, pending });

        Assert.Contains("PRAGMA foreign_keys = ON;", script, StringComparison.Ordinal);
        Assert.Contains("BEGIN IMMEDIATE;", script, StringComparison.Ordinal);
        Assert.Contains(applied.Id, script, StringComparison.Ordinal);
        Assert.Contains(applied.Checksum, script, StringComparison.Ordinal);
        Assert.Contains(pending.Sql, script, StringComparison.Ordinal);
        Assert.Contains(pending.Id, script, StringComparison.Ordinal);
        Assert.Contains(pending.Checksum, script, StringComparison.Ordinal);
        Assert.EndsWith(
            $"COMMIT;{Environment.NewLine}",
            script,
            StringComparison.Ordinal);
    }
}
