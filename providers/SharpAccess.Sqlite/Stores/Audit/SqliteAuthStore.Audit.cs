using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Writes one bounded security audit record.
    public async Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(connection, null, audit, cancellationToken).ConfigureAwait(false);
    }

    // Inserts one audit row on the caller's connection and transaction.
    private static Task<int> InsertAuditAsync(
        SqliteConnection connection,
        DbTransaction? transaction,
        AuditRecord audit,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_security_audit_logs(
                id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail)
            VALUES($id,$created,$eventType,$userId,$tenantId,$ipAddress,$userAgent,$detail);
            """,
            cancellationToken,
            ("$id", audit.Id.ToString("D")),
            ("$created", Format(audit.CreatedUtc)),
            ("$eventType", audit.EventType),
            ("$userId", audit.UserId?.ToString("D")),
            ("$tenantId", audit.TenantId?.ToString("D")),
            ("$ipAddress", audit.IpAddress),
            ("$userAgent", audit.UserAgent),
            ("$detail", audit.Detail));

    // Lists audit records in reverse chronological order.
    public async Task<AuthPageSlice<AuditRecord>> ListAuditAsync(
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(AuditRecord Item, AuthPageBoundary Boundary)> records = [];
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = page.After is null
            ? "SELECT id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail FROM auth_security_audit_logs ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;"
            : "SELECT id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail FROM auth_security_audit_logs WHERE created_utc < $afterCreated OR (created_utc = $afterCreated AND id > $afterId) ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;";
        if (page.After is not null)
        {
            AddParameter(command, "$afterCreated", Format(page.After.CreatedUtc));
            AddParameter(command, "$afterId", page.After.Id.ToString("D"));
        }
        AddParameter(command, "$fetchLimit", fetchLimit);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            AuditRecord record = new(
                ParseGuid(reader.GetString(0)),
                ParseDate(reader.GetString(1)),
                reader.GetString(2),
                ReadNullableGuid(reader, 3),
                ReadNullableGuid(reader, 4),
                ReadNullableString(reader, 5),
                ReadNullableString(reader, 6),
                ReadNullableString(reader, 7));
            records.Add((record, new AuthPageBoundary(record.CreatedUtc, record.Id)));
        }

        return AuthPageSupport.CreateSlice(records, pageLimit);
    }
}
