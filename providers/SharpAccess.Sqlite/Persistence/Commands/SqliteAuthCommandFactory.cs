using System.Data;
using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Sqlite.Migrations;

namespace SharpAccess.Sqlite;

internal sealed class SqliteAuthCommandFactory : IAuthCommandFactory
{
    // Creates a SQLite command through provider-neutral ADO.NET types.
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
