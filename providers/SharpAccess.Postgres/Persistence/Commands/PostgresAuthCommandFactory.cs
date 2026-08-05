using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Postgres.Migrations;

namespace SharpAccess.Postgres;

internal sealed class PostgresAuthCommandFactory : IAuthCommandFactory
{
    // Creates a PostgreSQL command through provider-neutral ADO.NET types.
    public DbCommand Create(DbConnection connection, DbTransaction? transaction, string commandText)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        DbCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        return command;
    }
}
