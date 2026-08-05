using System.Data;
using System.Data.Common;
using System.Globalization;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Services;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Writes one bounded security audit record.
    public async Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await InsertAuditAsync(connection, null, audit, cancellationToken).ConfigureAwait(false);
    }

    // Inserts one audit row on the caller's connection and transaction.
    private static Task<int> InsertAuditAsync(
        NpgsqlConnection connection,
        DbTransaction? transaction,
        AuditRecord audit,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_security_audit_logs(
                id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail)
            VALUES(@id,@created,@eventType,@userId,@tenantId,@ipAddress,@userAgent,@detail);
            """,
            cancellationToken,
            ("@id", audit.Id),
            ("@created", ToUtc(audit.CreatedUtc)),
            ("@eventType", audit.EventType),
            ("@userId", audit.UserId),
            ("@tenantId", audit.TenantId),
            ("@ipAddress", audit.IpAddress),
            ("@userAgent", audit.UserAgent),
            ("@detail", audit.Detail));
    // Lists one bounded keyset page of audit records in reverse chronological order.
    public async Task<AuthPageSlice<AuditRecord>> ListAuditAsync(
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(AuditRecord Item, AuthPageBoundary Boundary)> records = [];
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = page.After is null
            ? CreateCommand(connection, null,
                "SELECT id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail FROM auth_security_audit_logs ORDER BY created_utc DESC,id ASC LIMIT @fetchLimit;",
                ("@fetchLimit", fetchLimit))
            : CreateCommand(connection, null,
                "SELECT id,created_utc,event_type,user_id,tenant_id,ip_address,user_agent,detail FROM auth_security_audit_logs WHERE created_utc < @afterCreated OR (created_utc = @afterCreated AND id > @afterId) ORDER BY created_utc DESC,id ASC LIMIT @fetchLimit;",
                ("@afterCreated", ToUtc(page.After.CreatedUtc)),
                ("@afterId", page.After.Id),
                ("@fetchLimit", fetchLimit));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            AuditRecord audit = new(
                reader.GetGuid(0),
                ReadDate(reader, 1),
                reader.GetString(2),
                ReadNullableGuid(reader, 3),
                ReadNullableGuid(reader, 4),
                ReadNullableString(reader, 5),
                ReadNullableString(reader, 6),
                ReadNullableString(reader, 7));
            records.Add((audit, new AuthPageBoundary(audit.CreatedUtc, audit.Id)));
        }

        return AuthPageSupport.CreateSlice(records, pageLimit);
    }
}
