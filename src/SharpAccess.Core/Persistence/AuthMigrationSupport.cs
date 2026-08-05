using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;

namespace SharpAccess.Persistence;

// Captures provider migration-ledger state using provider-neutral values.
internal sealed record AuthMigrationLedgerSnapshot(
    bool MigrationLedgerExists,
    bool ChecksumLedgerExists,
    IReadOnlySet<string> AppliedMigrations,
    IReadOnlyDictionary<string, string> Checksums);

// Supplies provider-owned SQL and script formatting to the provider-neutral migration engine.
internal interface IAuthMigrationDialect
{
    string ProviderName { get; }
    bool UsesTransactionalDdl { get; }
    string MigrationLedgerExistsSql { get; }
    string ChecksumLedgerExistsSql { get; }
    string EnsureMigrationLedgerSql { get; }
    string EnsureChecksumLedgerSql { get; }
    string ReadAppliedMigrationsSql { get; }
    string ReadChecksumsSql { get; }
    string InsertAppliedMigrationSql { get; }
    string InsertChecksumSql { get; }
    string InsertChecksumIfMissingSql { get; }
    string? AcquireMigrationLockSql { get; }
    string? ReleaseMigrationLockSql { get; }

    // Converts one provider metadata scalar into a table-existence result.
    bool IsTablePresent(object? value);

    // Confirms that the provider-native migration lock was acquired.
    bool IsMigrationLockAcquired(object? value);

    // Formats a UTC migration timestamp for the concrete ADO.NET provider.
    object FormatAppliedUtc(DateTimeOffset value);

    // Builds one provider-native external migration script for the observed schema state.
    string BuildScript(SharpAccessSchemaStatus status, IReadOnlyList<AuthMigration> migrations);
}

// Implements migration, validation, status, and script generation for every relational provider.
internal sealed class AuthMigrationManager(
    Func<CancellationToken, ValueTask<DbConnection>> openConnection,
    IAuthMigrationProvider migrationProvider,
    IAuthMigrationDialect dialect) : IAuthSchemaManager
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessLocks = new(StringComparer.Ordinal);

    // Preserves the historical initialization contract by applying migrations.
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        MigrateAsync(cancellationToken);

    // Applies all pending provider-owned migrations under process and provider-native locks.
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        SemaphoreSlim processLock = ProcessLocks.GetOrAdd(dialect.ProviderName, static _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            processLock.Release();
        }
    }

    // Validates schema state using read-only metadata and ledger queries.
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        SharpAccessSchemaStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        AuthMigrationSupport.EnsureValid(status);
    }

    // Reads deterministic schema status without applying DDL or modifying migration ledgers.
    public async Task<SharpAccessSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = await openConnection(cancellationToken).ConfigureAwait(false);
        AuthMigrationLedgerSnapshot snapshot = await ReadSnapshotAsync(
            connection,
            null,
            cancellationToken).ConfigureAwait(false);
        return AuthMigrationSupport.CreateStatus(dialect.ProviderName, migrationProvider.GetMigrations(), snapshot);
    }

    // Generates a provider-native script for missing ledgers, checksum baselines, and pending migrations.
    public async Task<string> GenerateScriptAsync(CancellationToken cancellationToken = default)
    {
        SharpAccessSchemaStatus status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        AuthMigrationSupport.EnsureCanGenerateScript(status);
        return dialect.BuildScript(status, migrationProvider.GetMigrations());
    }

    // Runs one complete migration attempt while preserving the original failure if cleanup also fails.
    private async Task MigrateCoreAsync(CancellationToken cancellationToken)
    {
        await using DbConnection connection = await openConnection(cancellationToken).ConfigureAwait(false);
        DbTransaction? transaction = null;
        bool lockAcquired = false;
        Exception? operationFailure = null;
        try
        {
            if (dialect.UsesTransactionalDdl)
            {
                transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken).ConfigureAwait(false);
            }

            lockAcquired = await AcquireMigrationLockAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                dialect.EnsureMigrationLedgerSql,
                cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                dialect.EnsureChecksumLedgerSql,
                cancellationToken).ConfigureAwait(false);

            AuthMigrationLedgerSnapshot snapshot = await ReadSnapshotAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AuthMigration> migrations = migrationProvider.GetMigrations();
            SharpAccessSchemaStatus status = AuthMigrationSupport.CreateStatus(
                dialect.ProviderName,
                migrations,
                snapshot);
            AuthMigrationSupport.EnsureCanMigrate(status);

            foreach (AuthMigration migration in AuthMigrationSupport.GetMissingChecksumMigrations(status, migrations))
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    dialect.InsertChecksumIfMissingSql,
                    cancellationToken,
                    ("@id", migration.Id),
                    ("@checksum", migration.Checksum)).ConfigureAwait(false);
            }

            foreach (AuthMigration migration in AuthMigrationSupport.GetPendingMigrations(status, migrations))
            {
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    migration.Sql,
                    cancellationToken).ConfigureAwait(false);
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    dialect.InsertAppliedMigrationSql,
                    cancellationToken,
                    ("@id", migration.Id),
                    ("@appliedUtc", dialect.FormatAppliedUtc(DateTimeOffset.UtcNow))).ConfigureAwait(false);
                await ExecuteNonQueryAsync(
                    connection,
                    transaction,
                    dialect.InsertChecksumSql,
                    cancellationToken,
                    ("@id", migration.Id),
                    ("@checksum", migration.Checksum)).ConfigureAwait(false);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await transaction.DisposeAsync().ConfigureAwait(false);
                transaction = null;
            }

            AuthMigrationLedgerSnapshot finalSnapshot = await ReadSnapshotAsync(
                connection,
                null,
                cancellationToken).ConfigureAwait(false);
            AuthMigrationSupport.EnsureValid(AuthMigrationSupport.CreateStatus(
                dialect.ProviderName,
                migrations,
                finalSnapshot));
        }
        catch (Exception exception)
        {
            operationFailure = exception;
            if (transaction is not null)
            {
                await SafeRollbackAsync(transaction).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            if (lockAcquired && !string.IsNullOrWhiteSpace(dialect.ReleaseMigrationLockSql))
            {
                try
                {
                    await ExecuteNonQueryAsync(
                        connection,
                        null,
                        dialect.ReleaseMigrationLockSql,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    operationFailure is not null
                    && exception is DbException or InvalidOperationException)
                {
                    // Preserve the migration failure when a provider lock cannot be released cleanly.
                }
            }
        }
    }

    // Acquires one provider-native migration lock when the dialect requires it.
    private async Task<bool> AcquireMigrationLockAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dialect.AcquireMigrationLockSql))
        {
            return false;
        }

        object? value = await ExecuteScalarAsync(
            connection,
            transaction,
            dialect.AcquireMigrationLockSql,
            cancellationToken).ConfigureAwait(false);
        if (!dialect.IsMigrationLockAcquired(value))
        {
            throw new TimeoutException($"Could not acquire the {dialect.ProviderName} SharpAccess migration lock.");
        }

        return true;
    }

    // Reads provider ledgers only when the corresponding tables exist.
    private async Task<AuthMigrationLedgerSnapshot> ReadSnapshotAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        bool migrationLedgerExists = dialect.IsTablePresent(await ExecuteScalarAsync(
            connection,
            transaction,
            dialect.MigrationLedgerExistsSql,
            cancellationToken).ConfigureAwait(false));
        bool checksumLedgerExists = dialect.IsTablePresent(await ExecuteScalarAsync(
            connection,
            transaction,
            dialect.ChecksumLedgerExistsSql,
            cancellationToken).ConfigureAwait(false));

        HashSet<string> applied = new(StringComparer.Ordinal);
        if (migrationLedgerExists)
        {
            await using DbCommand command = CreateCommand(
                connection,
                transaction,
                dialect.ReadAppliedMigrationsSql);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                applied.Add(reader.GetString(0));
            }
        }

        Dictionary<string, string> checksums = new(StringComparer.Ordinal);
        if (checksumLedgerExists)
        {
            await using DbCommand command = CreateCommand(
                connection,
                transaction,
                dialect.ReadChecksumsSql);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                checksums[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return new AuthMigrationLedgerSnapshot(
            migrationLedgerExists,
            checksumLedgerExists,
            applied,
            checksums);
    }

    // Executes one provider-owned scalar command asynchronously.
    private static async Task<object?> ExecuteScalarAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = CreateCommand(connection, transaction, sql);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    // Executes one provider-owned parameterized command asynchronously.
    private static async Task<int> ExecuteNonQueryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Creates one provider-neutral command for trusted provider-owned SQL.
    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    // Rolls back best-effort without hiding the original migration failure.
    private static async Task SafeRollbackAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // Preserve the original migration failure.
        }
    }
}

// Centralizes immutable migration checksums and deterministic schema reports.
internal static class AuthMigrationSupport
{
    // Computes a line-ending-stable SHA-256 checksum for one immutable migration.
    internal static string ComputeChecksum(string id, string sql)
    {
        string normalizedSql = sql.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        byte[] bytes = Encoding.UTF8.GetBytes($"{id}\n{normalizedSql}");
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Creates one deterministic public status object from provider ledger state.
    internal static SharpAccessSchemaStatus CreateStatus(
        string providerName,
        IReadOnlyList<AuthMigration> migrations,
        AuthMigrationLedgerSnapshot snapshot)
    {
        Dictionary<string, AuthMigration> catalog = migrations.ToDictionary(
            static migration => migration.Id,
            StringComparer.Ordinal);
        string[] applied = snapshot.AppliedMigrations.Order(StringComparer.Ordinal).ToArray();
        string[] pending = migrations
            .Where(migration => !snapshot.AppliedMigrations.Contains(migration.Id))
            .Select(static migration => migration.Id)
            .ToArray();
        string[] unknown = snapshot.AppliedMigrations
            .Where(id => !catalog.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] missingChecksums = snapshot.AppliedMigrations
            .Where(catalog.ContainsKey)
            .Where(id => !snapshot.ChecksumLedgerExists || !snapshot.Checksums.ContainsKey(id))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] checksumMismatches = snapshot.Checksums
            .Where(pair => !snapshot.AppliedMigrations.Contains(pair.Key)
                || !catalog.TryGetValue(pair.Key, out AuthMigration? migration)
                || !string.Equals(pair.Value, migration.Checksum, StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new SharpAccessSchemaStatus(
            providerName,
            snapshot.MigrationLedgerExists,
            snapshot.ChecksumLedgerExists,
            applied,
            pending,
            unknown,
            missingChecksums,
            checksumMismatches);
    }

    // Rejects unknown or modified migrations before any provider-owned schema mutation.
    internal static void EnsureCanMigrate(SharpAccessSchemaStatus status)
    {
        if (status.UnknownMigrations.Count > 0 || status.ChecksumMismatches.Count > 0)
        {
            throw new InvalidOperationException(FormatFailure(status));
        }
    }

    // Rejects script generation on unknown or modified migration history.
    internal static void EnsureCanGenerateScript(SharpAccessSchemaStatus status) =>
        EnsureCanMigrate(status);

    // Requires a complete ledger, immutable checksums, and no pending migration.
    internal static void EnsureValid(SharpAccessSchemaStatus status)
    {
        if (!status.IsCurrent)
        {
            throw new InvalidOperationException(FormatFailure(status));
        }
    }

    // Selects pending migrations in immutable provider order.
    internal static IReadOnlyList<AuthMigration> GetPendingMigrations(
        SharpAccessSchemaStatus status,
        IReadOnlyList<AuthMigration> migrations)
    {
        HashSet<string> pending = status.PendingMigrations.ToHashSet(StringComparer.Ordinal);
        return migrations.Where(migration => pending.Contains(migration.Id)).ToArray();
    }

    // Selects applied migrations that need a one-time checksum baseline.
    internal static IReadOnlyList<AuthMigration> GetMissingChecksumMigrations(
        SharpAccessSchemaStatus status,
        IReadOnlyList<AuthMigration> migrations)
    {
        HashSet<string> missing = status.MissingChecksums.ToHashSet(StringComparer.Ordinal);
        return migrations.Where(migration => missing.Contains(migration.Id)).ToArray();
    }

    // Escapes one trusted migration identifier or checksum for generated SQL literals.
    internal static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    // Formats a bounded deterministic schema failure without database details or secrets.
    private static string FormatFailure(SharpAccessSchemaStatus status) =>
        $"SharpAccess schema validation failed for provider '{status.ProviderName}'. "
        + $"migration-ledger={(status.MigrationLedgerExists ? "present" : "missing")}; "
        + $"checksum-ledger={(status.ChecksumLedgerExists ? "present" : "missing")}; "
        + $"pending=[{string.Join(",", status.PendingMigrations)}]; "
        + $"unknown=[{string.Join(",", status.UnknownMigrations)}]; "
        + $"missing-checksums=[{string.Join(",", status.MissingChecksums)}]; "
        + $"checksum-mismatches=[{string.Join(",", status.ChecksumMismatches)}].";
}

// Resolves the safe environment default while preserving every explicit host selection.
internal static class SharpAccessMigrationModeResolver
{
    // Resolves Development and Test to apply-at-startup and every other environment to validate-only.
    internal static SharpAccessMigrationMode Resolve(SharpAccessMigrationMode? configuredMode, string? environmentName)
    {
        if (configuredMode.HasValue)
        {
            return configuredMode.Value;
        }

        return string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase)
            ? SharpAccessMigrationMode.ApplyAtStartup
            : SharpAccessMigrationMode.ValidateOnly;
    }
}
