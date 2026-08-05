using System.Data.Common;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthorizationStore
{
    // Reads string values from one effective authorization query.
    private async Task<IReadOnlyList<string>> ReadStringsAsync(
        string sql,
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateAuthorizationCommand(connection, sql, userId, tenantId);
        return await ReadAllStringsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static NpgsqlCommand CreateAuthorizationCommand(
        NpgsqlConnection connection,
        string sql,
        Guid userId,
        Guid? tenantId)
    {
        NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@userId", userId);
        if (tenantId.HasValue)
        {
            command.Parameters.AddWithValue("@tenantId", tenantId.Value);
        }

        return command;
    }

    private static async Task<IReadOnlyList<string>> ReadAllStringsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        List<string> values = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
