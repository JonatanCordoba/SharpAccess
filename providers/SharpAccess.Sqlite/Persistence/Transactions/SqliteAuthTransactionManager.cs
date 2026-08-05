using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Sqlite.Migrations;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthTransactionManager : IAuthTransactionManager
{
    // Executes and commits an asynchronous SQLite transaction or rolls it back safely.
    public async Task<T> ExecuteAsync<T>(
        DbConnection connection,
        IsolationLevel isolationLevel,
        Func<DbTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(operation);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(
            isolationLevel,
            cancellationToken).ConfigureAwait(false);
        try
        {
            T result = await operation(transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is DbException or InvalidOperationException)
            {
                // Preserve the original operation failure when rollback cannot complete.
            }

            throw;
        }
    }
}
