using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace SharpAccess.Sqlite;

internal sealed partial class SqliteAuthStore
{
    // Creates a tenant, owner membership, owner record, and tenant authorization catalog transactionally.
    public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        CreateTenantAsync(name, slug, ownerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_created", ownerUserId), cancellationToken);

    // Creates a tenant and commits its audit evidence with all initial ownership state.
    public async Task<TenantRecord?> CreateTenantAsync(
        string name,
        string slug,
        Guid ownerUserId,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        Guid tenantId = Guid.NewGuid();
        bool auditWriteStarted = false;
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int inserted = await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO auth_tenants(id,name,slug,created_utc)
                SELECT $id,$name,$slug,$created
                WHERE EXISTS(SELECT 1 FROM auth_users WHERE id=$ownerId AND is_active=1);
                """,
                cancellationToken,
                ("$id", tenantId.ToString("D")),
                ("$name", name),
                ("$slug", slug),
                ("$created", Format(now)),
                ("$ownerId", ownerUserId.ToString("D"))).ConfigureAwait(false);
            if (inserted != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertTenantMembershipInternalAsync(
                connection,
                transaction,
                tenantId,
                ownerUserId,
                now,
                cancellationToken).ConfigureAwait(false);
            await SeedTenantAuthorizationCatalogAsync(
                connection,
                transaction,
                tenantId,
                now,
                cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO auth_tenant_owners(tenant_id,user_id,assigned_utc) VALUES($tenantId,$ownerId,$assigned);",
                cancellationToken,
                ("$tenantId", tenantId.ToString("D")),
                ("$ownerId", ownerUserId.ToString("D")),
                ("$assigned", Format(now))).ConfigureAwait(false);
            await AssignTenantSystemRoleInternalAsync(
                connection,
                transaction,
                tenantId,
                ownerUserId,
                TenantOwnerRoleId,
                now,
                cancellationToken).ConfigureAwait(false);
            auditWriteStarted = true;
            await InsertAuditAsync(connection, transaction, audit with { TenantId = tenantId }, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new TenantRecord(tenantId, name, slug, now);
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

    // Lists tenants available to one member through an N+1 keyset query.
    public async Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(
        Guid userId,
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(TenantRecord Item, AuthPageBoundary Boundary)> tenants = [];
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = page.After is null
            ? "SELECT t.id,t.name,t.slug,t.created_utc,m.created_utc FROM auth_tenants t INNER JOIN auth_tenant_memberships m ON m.tenant_id=t.id WHERE m.user_id=$userId ORDER BY m.created_utc DESC,t.id ASC LIMIT $fetchLimit;"
            : "SELECT t.id,t.name,t.slug,t.created_utc,m.created_utc FROM auth_tenants t INNER JOIN auth_tenant_memberships m ON m.tenant_id=t.id WHERE m.user_id=$userId AND (m.created_utc < $afterCreated OR (m.created_utc = $afterCreated AND t.id > $afterId)) ORDER BY m.created_utc DESC,t.id ASC LIMIT $fetchLimit;";
        AddParameter(command, "$userId", userId.ToString("D"));
        if (page.After is not null)
        {
            AddParameter(command, "$afterCreated", Format(page.After.CreatedUtc));
            AddParameter(command, "$afterId", page.After.Id.ToString("D"));
        }
        AddParameter(command, "$fetchLimit", fetchLimit);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            TenantRecord tenant = MapTenant(reader);
            tenants.Add((tenant, new AuthPageBoundary(ParseDate(reader.GetString(4)), tenant.Id)));
        }

        return AuthPageSupport.CreateSlice(tenants, pageLimit);
    }

    // Finds a tenant by identifier.
    public async Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,slug,created_utc FROM auth_tenants WHERE id=$tenantId LIMIT 1;";
        AddParameter(command, "$tenantId", tenantId.ToString("D"));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapTenant(reader) : null;
    }

    // Gets the immutable owner record for one tenant.
    public async Task<TenantOwnerRecord?> GetTenantOwnerAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id,user_id,assigned_utc FROM auth_tenant_owners WHERE tenant_id=$tenantId LIMIT 1;";
        AddParameter(command, "$tenantId", tenantId.ToString("D"));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new TenantOwnerRecord(
                ParseGuid(reader.GetString(0)),
                ParseGuid(reader.GetString(1)),
                ParseDate(reader.GetString(2)))
            : null;
    }

    // Atomically transfers the owner record and immutable Owner role to an existing active member.
    public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        TransferTenantOwnershipAsync(tenantId, currentOwnerUserId, newOwnerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_ownership_transferred", newOwnerUserId, tenantId), cancellationToken);

    // Transfers ownership and commits its audit evidence with both users' session invalidation.
    public async Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(
        Guid tenantId,
        Guid currentOwnerUserId,
        Guid newOwnerUserId,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        if (currentOwnerUserId == newOwnerUserId)
        {
            return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.SameOwner);
        }

        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteCommand ownerCommand = CreateCommand(
                connection,
                transaction,
                "SELECT user_id FROM auth_tenant_owners WHERE tenant_id=$tenantId LIMIT 1;",
                ("$tenantId", tenantId.ToString("D")));
            object? ownerValue = await ownerCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (ownerValue is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.TenantNotFound);
            }

            Guid persistedOwner = ParseGuid(Convert.ToString(ownerValue, System.Globalization.CultureInfo.InvariantCulture)!);
            if (persistedOwner != currentOwnerUserId)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(
                    TenantOwnershipTransferStatus.CurrentOwnerMismatch,
                    persistedOwner,
                    newOwnerUserId);
            }

            await using SqliteCommand memberCommand = CreateCommand(
                connection,
                transaction,
                "SELECT 1 FROM auth_tenant_memberships WHERE tenant_id=$tenantId AND user_id=$userId LIMIT 1;",
                ("$tenantId", tenantId.ToString("D")),
                ("$userId", newOwnerUserId.ToString("D")));
            if (await memberCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(
                    TenantOwnershipTransferStatus.NewOwnerNotMember,
                    currentOwnerUserId,
                    newOwnerUserId);
            }

            int updated = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE auth_tenant_owners
                SET user_id=$newOwnerId,assigned_utc=$assigned
                WHERE tenant_id=$tenantId AND user_id=$currentOwnerId;
                """,
                cancellationToken,
                ("$newOwnerId", newOwnerUserId.ToString("D")),
                ("$assigned", Format(now)),
                ("$tenantId", tenantId.ToString("D")),
                ("$currentOwnerId", currentOwnerUserId.ToString("D"))).ConfigureAwait(false);
            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.CurrentOwnerMismatch);
            }

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM auth_tenant_user_roles WHERE tenant_id=$tenantId AND user_id=$userId AND role_id=$roleId;",
                cancellationToken,
                ("$tenantId", tenantId.ToString("D")),
                ("$userId", currentOwnerUserId.ToString("D")),
                ("$roleId", TenantOwnerRoleId)).ConfigureAwait(false);
            await AssignTenantSystemRoleInternalAsync(
                connection,
                transaction,
                tenantId,
                currentOwnerUserId,
                TenantMemberRoleId,
                now,
                cancellationToken).ConfigureAwait(false);
            await AssignTenantSystemRoleInternalAsync(
                connection,
                transaction,
                tenantId,
                newOwnerUserId,
                TenantOwnerRoleId,
                now,
                cancellationToken).ConfigureAwait(false);

            await InvalidateUserSessionsInternalAsync(
                connection,
                transaction,
                currentOwnerUserId,
                now,
                cancellationToken).ConfigureAwait(false);
            await InvalidateUserSessionsInternalAsync(
                connection,
                transaction,
                newOwnerUserId,
                now,
                cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new TenantOwnershipTransferResult(
                TenantOwnershipTransferStatus.Success,
                currentOwnerUserId,
                newOwnerUserId);
        }
        catch
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    // Adds a tenant member and assigns the standard tenant member role.
    public Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AddTenantMemberAsync(tenantId, userId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_member_added", userId, tenantId), cancellationToken);

    // Adds a tenant member and commits its audit evidence with the standard role.
    public async Task<bool> AddTenantMemberAsync(
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        AuditRecord audit,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool auditWriteStarted = false;
        try
        {
            int inserted = await InsertTenantMembershipInternalAsync(
                connection,
                transaction,
                tenantId,
                userId,
                now,
                cancellationToken).ConfigureAwait(false);
            if (inserted != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            int assigned = await AssignTenantSystemRoleInternalAsync(
                connection,
                transaction,
                tenantId,
                userId,
                TenantMemberRoleId,
                now,
                cancellationToken).ConfigureAwait(false);
            if (assigned != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

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

    // Lists tenant members and groups roles after selecting an N+1 membership keyset page.
    public async Task<AuthPageSlice<TenantMemberRecord>> ListTenantMembersAsync(
        Guid tenantId,
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        Dictionary<Guid, (string Email, bool IsOwner, DateTimeOffset CreatedUtc, List<string> Roles)> members = [];
        List<Guid> order = [];
        string pagePredicate = page.After is null
            ? string.Empty
            : " AND (created_utc < $afterCreated OR (created_utc = $afterCreated AND user_id > $afterId))";
        await using SqliteConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            WITH page AS (
                SELECT tenant_id,user_id,created_utc
                FROM auth_tenant_memberships
                WHERE tenant_id=$tenantId
                  {pagePredicate}
                ORDER BY created_utc DESC,user_id ASC
                LIMIT $fetchLimit
            )
            SELECT u.id,u.email,CASE WHEN o.user_id IS NULL THEN 0 ELSE 1 END,r.name,page.created_utc
            FROM page
            INNER JOIN auth_users u ON u.id=page.user_id
            LEFT JOIN auth_tenant_owners o ON o.tenant_id=page.tenant_id AND o.user_id=page.user_id
            LEFT JOIN auth_tenant_user_roles ur ON ur.user_id=u.id AND ur.tenant_id=page.tenant_id
            LEFT JOIN auth_tenant_roles r ON r.tenant_id=ur.tenant_id AND r.id=ur.role_id
            ORDER BY page.created_utc DESC,page.user_id ASC,r.name;
            """;
        AddParameter(command, "$tenantId", tenantId.ToString("D"));
        if (page.After is not null)
        {
            AddParameter(command, "$afterCreated", Format(page.After.CreatedUtc));
            AddParameter(command, "$afterId", page.After.Id.ToString("D"));
        }
        AddParameter(command, "$fetchLimit", fetchLimit);
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid userId = ParseGuid(reader.GetString(0));
            if (!members.TryGetValue(userId, out (string Email, bool IsOwner, DateTimeOffset CreatedUtc, List<string> Roles) value))
            {
                value = (reader.GetString(1), reader.GetInt64(2) != 0, ParseDate(reader.GetString(4)), []);
                members[userId] = value;
                order.Add(userId);
            }

            if (!reader.IsDBNull(3))
            {
                value.Roles.Add(reader.GetString(3));
            }
        }

        List<(TenantMemberRecord Item, AuthPageBoundary Boundary)> fetched = order.Select(userId =>
        {
            (string Email, bool IsOwner, DateTimeOffset CreatedUtc, List<string> Roles) member = members[userId];
            return (
                new TenantMemberRecord(userId, member.Email, member.IsOwner, member.Roles),
                new AuthPageBoundary(member.CreatedUtc, userId));
        }).ToList();
        return AuthPageSupport.CreateSlice(fetched, pageLimit);
    }

    // Seeds the immutable built-in tenant permission and role catalogs for one new tenant.
    private static async Task SeedTenantAuthorizationCatalogAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string created = Format(now);
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc) VALUES
                ($tenantId,$tenantReadId,'tenant.read','Read the active tenant.',$created),
                ($tenantId,$membersReadId,'tenant.members.read','Read active-tenant members.',$created),
                ($tenantId,$membersManageId,'tenant.members.manage','Manage active-tenant members.',$created),
                ($tenantId,$rolesManageId,'tenant.roles.manage','Manage active-tenant role assignments.',$created),
                ($tenantId,$ownershipTransferId,'tenant.owner.transfer','Transfer active-tenant ownership.',$created);
            """,
            cancellationToken,
            ("$tenantId", tenantId.ToString("D")),
            ("$tenantReadId", TenantReadPermissionId),
            ("$membersReadId", TenantMembersReadPermissionId),
            ("$membersManageId", TenantMembersManagePermissionId),
            ("$rolesManageId", TenantRolesManagePermissionId),
            ("$ownershipTransferId", TenantOwnershipTransferPermissionId),
            ("$created", created)).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc) VALUES
                ($tenantId,$ownerId,'Owner','OWNER','Immutable tenant owner role.',1,$created),
                ($tenantId,$managerId,'Manager','MANAGER','Manage members and tenant role assignments.',1,$created),
                ($tenantId,$memberId,'Member','MEMBER','Standard tenant member access.',1,$created);
            """,
            cancellationToken,
            ("$tenantId", tenantId.ToString("D")),
            ("$ownerId", TenantOwnerRoleId),
            ("$managerId", TenantManagerRoleId),
            ("$memberId", TenantMemberRoleId),
            ("$created", created)).ConfigureAwait(false);
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc) VALUES
                ($tenantId,$ownerId,$tenantReadId,$created),
                ($tenantId,$ownerId,$membersReadId,$created),
                ($tenantId,$ownerId,$membersManageId,$created),
                ($tenantId,$ownerId,$rolesManageId,$created),
                ($tenantId,$ownerId,$ownershipTransferId,$created),
                ($tenantId,$managerId,$tenantReadId,$created),
                ($tenantId,$managerId,$membersReadId,$created),
                ($tenantId,$managerId,$membersManageId,$created),
                ($tenantId,$managerId,$rolesManageId,$created),
                ($tenantId,$memberId,$tenantReadId,$created),
                ($tenantId,$memberId,$membersReadId,$created);
            """,
            cancellationToken,
            ("$tenantId", tenantId.ToString("D")),
            ("$ownerId", TenantOwnerRoleId),
            ("$managerId", TenantManagerRoleId),
            ("$memberId", TenantMemberRoleId),
            ("$tenantReadId", TenantReadPermissionId),
            ("$membersReadId", TenantMembersReadPermissionId),
            ("$membersManageId", TenantMembersManagePermissionId),
            ("$rolesManageId", TenantRolesManagePermissionId),
            ("$ownershipTransferId", TenantOwnershipTransferPermissionId),
            ("$created", created)).ConfigureAwait(false);
    }

    // Assigns one immutable built-in role to an active tenant member.
    private static Task<int> AssignTenantSystemRoleInternalAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        Guid tenantId,
        Guid userId,
        string roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT OR IGNORE INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
            SELECT $id,m.tenant_id,m.user_id,r.id,$created
            FROM auth_tenant_memberships m
            INNER JOIN auth_tenant_roles r ON r.tenant_id=m.tenant_id
            WHERE m.tenant_id=$tenantId AND m.user_id=$userId AND r.id=$roleId;
            """,
            cancellationToken,
            ("$id", Guid.NewGuid().ToString("D")),
            ("$created", Format(now)),
            ("$tenantId", tenantId.ToString("D")),
            ("$userId", userId.ToString("D")),
            ("$roleId", roleId));
}
