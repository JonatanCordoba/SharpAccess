using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using Npgsql;

namespace SharpAccess.Postgres;

internal interface IPostgresAuthConnectionFactory
{
    // Opens a PostgreSQL connection using the selected provider connection source.
    ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

internal sealed class PostgresAuthConnectionFactory : IPostgresAuthConnectionFactory, IAuthConnectionFactory, IDisposable, IAsyncDisposable
{
    private const string ProviderApplicationName = "SharpAccess.Postgres";
    private readonly NpgsqlDataSource? _dataSource;
    private readonly Func<CancellationToken, ValueTask<NpgsqlConnection>>? _connectionFactory;
    private readonly bool _ownsDataSource;
    private int _disposed;

    // Creates one provider-owned pooled data source from validated provider options.
    internal PostgresAuthConnectionFactory(PostgresAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        NpgsqlConnectionStringBuilder builder = new(options.ConnectionString)
        {
            Timezone = "UTC"
        };
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = ProviderApplicationName;
        }

        _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        _ownsDataSource = true;
    }

    // Creates a connection factory around a host-owned data source without transferring ownership.
    internal PostgresAuthConnectionFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    // Creates a PostgreSQL connection factory from host-managed logical connection creation.
    internal PostgresAuthConnectionFactory(Func<CancellationToken, ValueTask<NpgsqlConnection>> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    internal bool UsesDataSource => _dataSource is not null;
    internal bool OwnsDataSource => _ownsDataSource;

    // Opens a PostgreSQL connection using the selected provider connection source.
    public async ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_dataSource is not null)
        {
            return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlConnection connection = await _connectionFactory!(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The PostgreSQL connection factory returned null.");
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Opens a PostgreSQL connection through the provider-neutral connection-factory contract.
    async ValueTask<DbConnection> IAuthConnectionFactory.OpenAsync(CancellationToken cancellationToken)
    {
        return await OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    // Supports synchronous service-provider disposal without disposing a host-owned data source.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsDataSource)
        {
            _dataSource?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // Disposes asynchronously only a data source created and owned by the provider registration.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsDataSource && _dataSource is not null)
        {
            await _dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Prevents use after the owning service provider has disposed this factory.
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
