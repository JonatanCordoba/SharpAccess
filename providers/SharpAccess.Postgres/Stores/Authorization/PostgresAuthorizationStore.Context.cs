using System.Globalization;
using SharpAccess.Domain;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthorizationStore
{
    // Gets separately categorized global and active-tenant authorization data.
    internal async Task<EffectiveAuthorizationContext> GetEffectiveAuthorizationContextAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> globalRoles = await GetGlobalRolesAsync(userId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> globalPermissions = await GetGlobalPermissionsAsync(userId, cancellationToken).ConfigureAwait(false);
        long authorizationVersion = await ReadAuthorizationVersionAsync(userId, cancellationToken).ConfigureAwait(false);

        TenantAuthorizationContext? tenant = null;
        if (tenantId.HasValue)
        {
            IReadOnlyList<string> tenantRoles = await GetTenantRolesAsync(
                userId,
                tenantId.Value,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> tenantPermissions = await GetTenantPermissionsAsync(
                userId,
                tenantId.Value,
                cancellationToken).ConfigureAwait(false);
            bool isOwner = await IsTenantOwnerAsync(
                userId,
                tenantId.Value,
                cancellationToken).ConfigureAwait(false);
            tenant = new TenantAuthorizationContext(
                tenantId.Value,
                isOwner,
                tenantRoles,
                tenantPermissions);
        }

        return new EffectiveAuthorizationContext(
            new GlobalAuthorizationContext(globalRoles, globalPermissions),
            tenant,
            authorizationVersion);
    }

    // Reads the persisted authorization version for one user.
    private async Task<long> ReadAuthorizationVersionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = AuthorizationVersionSql;
        command.Parameters.AddWithValue("@userId", userId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    // Checks the unique owner record for one active tenant.
    private async Task<bool> IsTenantOwnerAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = TenantOwnerSql;
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@tenantId", tenantId);
        object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is bool isOwner && isOwner;
    }
}
