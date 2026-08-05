using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    // Assigns one global role during registration or OAuth provisioning without tenant ambiguity.
    private static Task<int> AssignGlobalRoleInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_global_user_roles(id,user_id,role_id,created_utc)
            SELECT @id,u.id,r.id,@now
            FROM auth_users u CROSS JOIN auth_global_roles r
            WHERE u.id=@userId AND u.is_active=true AND r.id=@roleId
            ON CONFLICT(user_id,role_id) DO NOTHING;
            """,
            cancellationToken,
            ("@id", Guid.NewGuid()),
            ("@userId", userId),
            ("@roleId", roleId),
            ("@now", ToUtc(now)));
}
