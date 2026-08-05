using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Preserves PostgreSQL UUID type metadata when an optional tenant identifier is null.
    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        string sql,
        (string Name, Guid Value) requiredGuid,
        (string Name, Guid? Value) optionalGuid)
    {
        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (NpgsqlTransaction?)transaction;
        command.Parameters.Add(new NpgsqlParameter(requiredGuid.Name, NpgsqlDbType.Uuid)
        {
            Value = requiredGuid.Value
        });
        command.Parameters.Add(new NpgsqlParameter(optionalGuid.Name, NpgsqlDbType.Uuid)
        {
            Value = optionalGuid.Value.HasValue
                ? (object)optionalGuid.Value.Value
                : DBNull.Value
        });
        return command;
    }
}
