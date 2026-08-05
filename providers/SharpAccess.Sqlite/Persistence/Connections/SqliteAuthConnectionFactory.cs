using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SharpAccess.Sqlite;

internal interface ISqliteAuthConnectionFactory
{
    // Opens a SQLite connection and applies the bounded provider connection policy.
    ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default);
}

internal sealed class SqliteAuthConnectionFactory : ISqliteAuthConnectionFactory, IAuthConnectionFactory
{
    private readonly Func<CancellationToken, ValueTask<SqliteConnection>> _connectionFactory;
    private readonly bool _applyProviderOwnedFilePolicy;

    // Creates a connection factory from provider-owned options.
    internal SqliteAuthConnectionFactory(IOptions<SqliteAuthOptions> options)
        : this(CreateConfiguredConnectionFactory(options), applyProviderOwnedFilePolicy: true)
    {
    }

    // Creates a connection factory from a host-managed logical-connection delegate.
    internal SqliteAuthConnectionFactory(
        Func<CancellationToken, ValueTask<SqliteConnection>> connectionFactory)
        : this(connectionFactory, applyProviderOwnedFilePolicy: false)
    {
    }

    // Captures logical connection creation and whether SharpAccess owns file-level SQLite policy.
    private SqliteAuthConnectionFactory(
        Func<CancellationToken, ValueTask<SqliteConnection>> connectionFactory,
        bool applyProviderOwnedFilePolicy)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _applyProviderOwnedFilePolicy = applyProviderOwnedFilePolicy;
    }

    // Opens a SQLite connection and applies foreign-key, timeout, and owned file-database policy.
    public async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        SqliteConnection connection = await _connectionFactory(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The SQLite connection factory returned null.");
        try
        {
            EnsureDatabaseDirectory(connection.ConnectionString);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await ConfigureConnectionAsync(
                connection,
                _applyProviderOwnedFilePolicy,
                cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Opens a SQLite connection through the provider-neutral connection-factory contract.
    async ValueTask<DbConnection> IAuthConnectionFactory.OpenAsync(CancellationToken cancellationToken)
    {
        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    // Applies mandatory per-connection settings and provider-owned file policy when applicable.
    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        bool applyProviderOwnedFilePolicy,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (!applyProviderOwnedFilePolicy || !IsWritableFileDatabase(connection.ConnectionString))
        {
            return;
        }

        command.CommandText = "PRAGMA journal_mode = WAL;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        string journalMode = Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite refused the required WAL journal mode and returned '{journalMode}'.");
        }

        command.CommandText = "PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Identifies writable file-backed databases that may receive provider-owned journal settings.
    private static bool IsWritableFileDatabase(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        string dataSource = builder.DataSource;
        return builder.Mode is not SqliteOpenMode.Memory and not SqliteOpenMode.ReadOnly
            && !string.IsNullOrWhiteSpace(dataSource)
            && !string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            && !dataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);
    }

    // Creates logical SQLite connections from the configured connection string.
    private static Func<CancellationToken, ValueTask<SqliteConnection>> CreateConfiguredConnectionFactory(
        IOptions<SqliteAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string connectionString = options.Value.ConnectionString;
        return _ => ValueTask.FromResult(new SqliteConnection(connectionString));
    }

    // Creates the parent directory for ordinary file-backed SQLite databases before opening the connection.
    private static void EnsureDatabaseDirectory(string connectionString)
    {
        SqliteConnectionStringBuilder builder = new(connectionString);
        string dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
