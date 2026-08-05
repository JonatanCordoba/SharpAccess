using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Gets separately categorized global and active-tenant authorization data in one provider-owned read.
    public async Task<EffectiveAuthorizationContext> GetEffectiveAuthorizationContextAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 'version' AS kind,'' AS name,u.security_version AS authorization_version
            FROM auth_users u
            WHERE u.id=$userId
            UNION ALL
            SELECT 'global_role',r.name,u.security_version
            FROM auth_global_roles r
            INNER JOIN auth_global_user_roles ur ON ur.role_id=r.id
            INNER JOIN auth_users u ON u.id=ur.user_id
            WHERE ur.user_id=$userId
            UNION ALL
            SELECT 'global_permission',p.name,u.security_version
            FROM auth_global_permissions p
            INNER JOIN auth_global_role_permissions rp ON rp.permission_id=p.id
            INNER JOIN auth_global_user_roles ur ON ur.role_id=rp.role_id
            INNER JOIN auth_users u ON u.id=ur.user_id
            WHERE ur.user_id=$userId
            UNION ALL
            SELECT 'tenant_owner','',u.security_version
            FROM auth_tenant_owners o
            INNER JOIN auth_users u ON u.id=o.user_id
            WHERE $tenantId IS NOT NULL AND o.tenant_id=$tenantId AND o.user_id=$userId
            UNION ALL
            SELECT 'tenant_role',r.name,u.security_version
            FROM auth_tenant_roles r
            INNER JOIN auth_tenant_user_roles ur
                ON ur.tenant_id=r.tenant_id AND ur.role_id=r.id
            INNER JOIN auth_users u ON u.id=ur.user_id
            WHERE $tenantId IS NOT NULL AND ur.tenant_id=$tenantId AND ur.user_id=$userId
            UNION
            SELECT 'tenant_permission',p.name,u.security_version
            FROM auth_tenant_permissions p
            INNER JOIN auth_tenant_role_permissions rp
                ON rp.tenant_id=p.tenant_id AND rp.permission_id=p.id
            INNER JOIN auth_tenant_user_roles ur
                ON ur.tenant_id=rp.tenant_id AND ur.role_id=rp.role_id
            INNER JOIN auth_users u ON u.id=ur.user_id
            WHERE $tenantId IS NOT NULL AND ur.tenant_id=$tenantId AND ur.user_id=$userId
            ORDER BY kind,name;
            """;

        List<string> globalRoles = [];
        List<string> globalPermissions = [];
        List<string> tenantRoles = [];
        List<string> tenantPermissions = [];
        bool isOwner = false;
        long authorizationVersion = 0;
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$userId", userId.ToString("D"));
        AddParameter(command, "$tenantId", tenantId?.ToString("D"));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string kind = reader.GetString(0);
            string name = reader.GetString(1);
            authorizationVersion = reader.GetInt64(2);
            switch (kind)
            {
                case "global_role":
                    globalRoles.Add(name);
                    break;
                case "global_permission":
                    globalPermissions.Add(name);
                    break;
                case "tenant_owner":
                    isOwner = true;
                    break;
                case "tenant_role":
                    tenantRoles.Add(name);
                    break;
                case "tenant_permission":
                    tenantPermissions.Add(name);
                    break;
            }
        }

        TenantAuthorizationContext? tenant = tenantId.HasValue
            ? new TenantAuthorizationContext(
                tenantId.Value,
                isOwner,
                tenantRoles.Distinct(StringComparer.Ordinal).ToArray(),
                tenantPermissions.Distinct(StringComparer.Ordinal).ToArray())
            : null;
        return new EffectiveAuthorizationContext(
            new GlobalAuthorizationContext(
                globalRoles.Distinct(StringComparer.Ordinal).ToArray(),
                globalPermissions.Distinct(StringComparer.Ordinal).ToArray()),
            tenant,
            authorizationVersion);
    }

    // Checks active membership using only persisted server-side data.
    public async Task<bool> IsTenantMemberAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM auth_tenant_memberships WHERE user_id=$userId AND tenant_id=$tenantId LIMIT 1;";
        AddParameter(command, "$userId", userId.ToString("D"));
        AddParameter(command, "$tenantId", tenantId.ToString("D"));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    // Lists global roles with stable creation ordering and an N+1 keyset query.
    public async Task<AuthPageSlice<RoleRecord>> ListRolesAsync(
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(RoleRecord Item, AuthPageBoundary Boundary)> roles = [];
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = page.After is null
            ? "SELECT id,name,description,is_system,created_utc FROM auth_global_roles ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;"
            : "SELECT id,name,description,is_system,created_utc FROM auth_global_roles WHERE created_utc < $afterCreated OR (created_utc = $afterCreated AND id > $afterId) ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;";
        if (page.After is not null)
        {
            AddParameter(command, "$afterCreated", Format(page.After.CreatedUtc));
            AddParameter(command, "$afterId", page.After.Id.ToString("D"));
        }
        AddParameter(command, "$fetchLimit", fetchLimit);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid id = ParseGuid(reader.GetString(0));
            RoleRecord role = new(
                id,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0);
            roles.Add((role, new AuthPageBoundary(ParseDate(reader.GetString(4)), id)));
        }

        return AuthPageSupport.CreateSlice(roles, pageLimit);
    }

    // Creates a dynamic global role and returns null on normalized-name conflict.
    public Task<RoleRecord?> CreateRoleAsync(
        string name,
        string normalizedName,
        string description,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        CreateRoleAsync(name, normalizedName, description, now, SecurityAuditEvidence.ForStoreMutation(now, "global_role_created"), cancellationToken);

    // Creates a dynamic global role and its audit evidence atomically.
    public async Task<RoleRecord?> CreateRoleAsync(
        string name,
        string normalizedName,
        string description,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        Guid id = Guid.NewGuid();
        bool auditWriteStarted = false;
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO auth_global_roles(id,name,normalized_name,description,is_system,created_utc)
                VALUES($id,$name,$normalized,$description,0,$created);
                """,
                cancellationToken,
                ("$id", id.ToString("D")),
                ("$name", name),
                ("$normalized", normalizedName),
                ("$description", description),
                ("$created", Format(now))).ConfigureAwait(false);
            auditWriteStarted = true;
            await InsertAuditAsync(connection, transaction, audit with { Detail = audit.Detail ?? $"role={id:D}" }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new RoleRecord(id, name, description, false);
        }
        catch (SqliteException exception) when (!auditWriteStarted && IsConstraintViolation(exception))
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            return null;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Updates only non-system global roles and invalidates sessions for every affected user.
    public Task<bool> UpdateRoleAsync(
        Guid roleId,
        string name,
        string normalizedName,
        string description,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpdateRoleAsync(roleId, name, normalizedName, description, now, SecurityAuditEvidence.ForStoreMutation(now, "global_role_updated"), cancellationToken);

    // Updates a role, invalidates affected sessions, and writes its audit evidence atomically.
    public async Task<bool> UpdateRoleAsync(
        Guid roleId,
        string name,
        string normalizedName,
        string description,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool auditWriteStarted = false;
        try
        {
            int affected = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE auth_global_roles
                SET name=$name,normalized_name=$normalized,description=$description
                WHERE id=$id AND is_system=0;
                """,
                cancellationToken,
                ("$name", name),
                ("$normalized", normalizedName),
                ("$description", description),
                ("$id", roleId.ToString("D"))).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await InvalidateGlobalRoleUsersAsync(connection, transaction, roleId, now, cancellationToken).ConfigureAwait(false);
            auditWriteStarted = true;
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException exception) when (!auditWriteStarted && IsConstraintViolation(exception))
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            return false;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Lists global permissions with stable creation ordering and an N+1 keyset query.
    public async Task<AuthPageSlice<PermissionRecord>> ListPermissionsAsync(
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(PermissionRecord Item, AuthPageBoundary Boundary)> permissions = [];
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = page.After is null
            ? "SELECT id,name,description,created_utc FROM auth_global_permissions ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;"
            : "SELECT id,name,description,created_utc FROM auth_global_permissions WHERE created_utc < $afterCreated OR (created_utc = $afterCreated AND id > $afterId) ORDER BY created_utc DESC,id ASC LIMIT $fetchLimit;";
        if (page.After is not null)
        {
            AddParameter(command, "$afterCreated", Format(page.After.CreatedUtc));
            AddParameter(command, "$afterId", page.After.Id.ToString("D"));
        }
        AddParameter(command, "$fetchLimit", fetchLimit);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid id = ParseGuid(reader.GetString(0));
            PermissionRecord permission = new(
                id,
                reader.GetString(1),
                reader.GetString(2));
            permissions.Add((permission, new AuthPageBoundary(ParseDate(reader.GetString(3)), id)));
        }

        return AuthPageSupport.CreateSlice(permissions, pageLimit);
    }

    // Assigns a global permission and invalidates sessions for every user holding the global role.
    public Task<bool> AssignPermissionToRoleAsync(
        Guid roleId,
        Guid permissionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        AssignPermissionToRoleAsync(roleId, permissionId, now, SecurityAuditEvidence.ForStoreMutation(now, "permission_changed"), cancellationToken);

    // Assigns a permission and commits its audit evidence with the session invalidation.
    public Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeGlobalRolePermissionAsync(roleId, permissionId, now, audit, assign: true, cancellationToken);

    // Removes a global permission and invalidates sessions for every user holding the global role.
    public Task<bool> RemovePermissionFromRoleAsync(
        Guid roleId,
        Guid permissionId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        RemovePermissionFromRoleAsync(roleId, permissionId, now, SecurityAuditEvidence.ForStoreMutation(now, "permission_changed"), cancellationToken);

    // Removes a permission and commits its audit evidence with the session invalidation.
    public Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeGlobalRolePermissionAsync(roleId, permissionId, now, audit, assign: false, cancellationToken);

    // Assigns a global role and invalidates all current sessions of the affected user.
    public Task<bool> AssignGlobalRoleToUserAsync(
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        AssignGlobalRoleToUserAsync(userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "role_assigned", userId), cancellationToken);

    // Assigns a global role and commits its audit evidence with the session invalidation.
    public Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeUserRoleAsync(userId, roleId, tenantId: null, now, audit, assign: true, cancellationToken);

    // Removes a global role and invalidates all current sessions of the affected user.
    public Task<bool> RemoveGlobalRoleFromUserAsync(
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        RemoveGlobalRoleFromUserAsync(userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "role_removed", userId), cancellationToken);

    // Removes a global role and commits its audit evidence with the session invalidation.
    public Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeUserRoleAsync(userId, roleId, tenantId: null, now, audit, assign: false, cancellationToken);

    // Assigns a tenant role only to an active member of the selected tenant.
    public Task<bool> AssignTenantRoleToUserAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        AssignTenantRoleToUserAsync(tenantId, userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_role_assigned", userId, tenantId), cancellationToken);

    // Assigns a tenant role and commits its audit evidence with the session invalidation.
    public Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeUserRoleAsync(userId, roleId, tenantId, now, audit, assign: true, cancellationToken);

    // Removes a tenant role without modifying tenant ownership.
    public Task<bool> RemoveTenantRoleFromUserAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        RemoveTenantRoleFromUserAsync(tenantId, userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_role_removed", userId, tenantId), cancellationToken);

    // Removes a tenant role and commits its audit evidence with the session invalidation.
    public Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
        ChangeUserRoleAsync(userId, roleId, tenantId, now, audit, assign: false, cancellationToken);

    // Applies one global role-permission change and invalidates affected global sessions.
    private async Task<bool> ChangeGlobalRolePermissionAsync(
        Guid roleId,
        Guid permissionId,
        DateTimeOffset now,
        AuditRecord audit,
        bool assign,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string sql = assign
                ? """
                  INSERT OR IGNORE INTO auth_global_role_permissions(role_id,permission_id,created_utc)
                  SELECT r.id,p.id,$created FROM auth_global_roles r CROSS JOIN auth_global_permissions p
                  WHERE r.id=$roleId AND p.id=$permissionId;
                  """
                : "DELETE FROM auth_global_role_permissions WHERE role_id=$roleId AND permission_id=$permissionId;";
            int affected = await ExecuteAsync(
                connection,
                transaction,
                sql,
                cancellationToken,
                ("$created", Format(now)),
                ("$roleId", roleId.ToString("D")),
                ("$permissionId", permissionId.ToString("D"))).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await InvalidateGlobalRoleUsersAsync(connection, transaction, roleId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Applies one explicitly scoped user-role change and invalidates the affected user.
    private async Task<bool> ChangeUserRoleAsync(
        Guid userId,
        Guid roleId,
        Guid? tenantId,
        DateTimeOffset now,
        AuditRecord audit,
        bool assign,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string sql = tenantId.HasValue
                ? assign
                    ? """
                      INSERT OR IGNORE INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
                      SELECT $id,m.tenant_id,m.user_id,r.id,$created
                      FROM auth_tenant_memberships m
                      INNER JOIN auth_tenant_roles r ON r.tenant_id=m.tenant_id
                      WHERE m.tenant_id=$tenantId AND m.user_id=$userId AND r.id=$roleId
                        AND r.id<>$ownerRoleId;
                      """
                    : "DELETE FROM auth_tenant_user_roles WHERE tenant_id=$tenantId AND user_id=$userId AND role_id=$roleId AND role_id<>$ownerRoleId;"
                : assign
                    ? """
                      INSERT OR IGNORE INTO auth_global_user_roles(id,user_id,role_id,created_utc)
                      SELECT $id,u.id,r.id,$created
                      FROM auth_users u CROSS JOIN auth_global_roles r
                      WHERE u.id=$userId AND u.is_active=1 AND r.id=$roleId;
                      """
                    : "DELETE FROM auth_global_user_roles WHERE user_id=$userId AND role_id=$roleId;";
            int affected = await ExecuteAsync(
                connection,
                transaction,
                sql,
                cancellationToken,
                ("$id", Guid.NewGuid().ToString("D")),
                ("$tenantId", tenantId?.ToString("D")),
                ("$userId", userId.ToString("D")),
                ("$roleId", roleId.ToString("D")),
                ("$ownerRoleId", TenantOwnerRoleId),
                ("$created", Format(now))).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await InvalidateUserSessionsInternalAsync(connection, transaction, userId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Invalidates every user assigned to one global role.
    private static async Task InvalidateGlobalRoleUsersAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_users
            SET security_version=security_version+1,updated_utc=$now
            WHERE id IN (SELECT DISTINCT user_id FROM auth_global_user_roles WHERE role_id=$roleId);
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$roleId", roleId.ToString("D"))).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE auth_refresh_tokens
            SET revoked_utc=$now
            WHERE revoked_utc IS NULL
              AND user_id IN (SELECT DISTINCT user_id FROM auth_global_user_roles WHERE role_id=$roleId);
            """,
            cancellationToken,
            ("$now", Format(now)),
            ("$roleId", roleId.ToString("D"))).ConfigureAwait(false);
    }
}
