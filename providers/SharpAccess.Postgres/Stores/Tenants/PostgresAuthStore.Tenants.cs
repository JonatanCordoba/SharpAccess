using System.Data.Common;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using Npgsql;

namespace SharpAccess.Postgres;

internal sealed partial class PostgresAuthStore
{
    private static readonly Guid TenantOwnerRoleId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantManagerRoleId = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid TenantMemberRoleId = Guid.Parse("40000000-0000-0000-0000-000000000003");

    // Checks active membership using only persisted server-side data.
    public async Task<bool> IsTenantMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(connection, null,
            "SELECT 1 FROM auth_tenant_memberships WHERE user_id=@userId AND tenant_id=@tenantId LIMIT 1;",
            ("@userId", userId), ("@tenantId", tenantId));
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    // Creates a tenant, owner membership, owner record, and tenant authorization catalog transactionally.
    public Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        CreateTenantAsync(name, slug, ownerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_created", ownerUserId), cancellationToken);
    // Creates a tenant and commits its audit evidence with initial ownership.
    public async Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default)
    {
        Guid tenantId = Guid.NewGuid();
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool auditWriteStarted = false;
        try
        {
            int inserted = await ExecuteAsync(connection, transaction,
                "INSERT INTO auth_tenants(id,name,slug,created_utc) SELECT @id,@name,@slug,@created WHERE EXISTS(SELECT 1 FROM auth_users WHERE id=@ownerId AND is_active=true);",
                cancellationToken,
                ("@id", tenantId), ("@name", name), ("@slug", slug), ("@created", ToUtc(now)), ("@ownerId", ownerUserId)).ConfigureAwait(false);
            if (inserted != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            await InsertTenantMembershipInternalAsync(connection, transaction, tenantId, ownerUserId, now, cancellationToken).ConfigureAwait(false);
            await SeedTenantAuthorizationCatalogAsync(connection, transaction, tenantId, now, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO auth_tenant_owners(tenant_id,user_id,assigned_utc) VALUES(@tenantId,@ownerId,@assigned);",
                cancellationToken,
                ("@tenantId", tenantId), ("@ownerId", ownerUserId), ("@assigned", ToUtc(now))).ConfigureAwait(false);
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
        catch (PostgresException exception) when (!auditWriteStarted && IsConstraintViolation(exception))
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

    // Lists one bounded keyset page of memberships for a user and returns their tenants.
    public async Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(
        Guid userId,
        AuthPageQuery page,
        CancellationToken cancellationToken = default)
    {
        int fetchLimit = AuthPageSupport.GetFetchLimit(page, out int pageLimit);
        List<(TenantRecord Item, AuthPageBoundary Boundary)> tenants = [];
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = page.After is null
            ? CreateCommand(connection, null,
                "SELECT t.id,t.name,t.slug,t.created_utc,m.created_utc FROM auth_tenants t INNER JOIN auth_tenant_memberships m ON m.tenant_id=t.id WHERE m.user_id=@userId ORDER BY m.created_utc DESC,t.id ASC LIMIT @fetchLimit;",
                ("@userId", userId),
                ("@fetchLimit", fetchLimit))
            : CreateCommand(connection, null,
                "SELECT t.id,t.name,t.slug,t.created_utc,m.created_utc FROM auth_tenants t INNER JOIN auth_tenant_memberships m ON m.tenant_id=t.id WHERE m.user_id=@userId AND (m.created_utc < @afterCreated OR (m.created_utc = @afterCreated AND t.id > @afterId)) ORDER BY m.created_utc DESC,t.id ASC LIMIT @fetchLimit;",
                ("@userId", userId),
                ("@afterCreated", ToUtc(page.After.CreatedUtc)),
                ("@afterId", page.After.Id),
                ("@fetchLimit", fetchLimit));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            TenantRecord tenant = MapTenant(reader);
            tenants.Add((tenant, new AuthPageBoundary(ReadDate(reader, 4), tenant.Id)));
        }
        return AuthPageSupport.CreateSlice(tenants, pageLimit);
    }

    // Finds a tenant by identifier.
    public async Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(connection, null,
            "SELECT id,name,slug,created_utc FROM auth_tenants WHERE id=@tenantId LIMIT 1;",
            ("@tenantId", tenantId));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapTenant(reader) : null;
    }

    // Gets the immutable owner record for one tenant.
    public async Task<TenantOwnerRecord?> GetTenantOwnerAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlCommand command = CreateCommand(connection, null,
            "SELECT tenant_id,user_id,assigned_utc FROM auth_tenant_owners WHERE tenant_id=@tenantId LIMIT 1;",
            ("@tenantId", tenantId));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new TenantOwnerRecord(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime())
            : null;
    }

    // Atomically transfers the owner record and immutable Owner role to an existing active tenant member.
    public Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        TransferTenantOwnershipAsync(tenantId, currentOwnerUserId, newOwnerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_ownership_transferred", newOwnerUserId, tenantId), cancellationToken);
    // Transfers ownership and commits its audit evidence with session invalidation.
    public async Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default)
    {
        if (currentOwnerUserId == newOwnerUserId)
        {
            return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.SameOwner);
        }

        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using NpgsqlCommand ownerCommand = CreateCommand(connection, transaction,
                "SELECT user_id FROM auth_tenant_owners WHERE tenant_id=@tenantId FOR UPDATE;",
                ("@tenantId", tenantId));
            object? ownerValue = await ownerCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (ownerValue is not Guid persistedOwner)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.TenantNotFound);
            }
            if (persistedOwner != currentOwnerUserId)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.CurrentOwnerMismatch, persistedOwner, newOwnerUserId);
            }

            await using NpgsqlCommand memberCommand = CreateCommand(connection, transaction,
                "SELECT 1 FROM auth_tenant_memberships WHERE tenant_id=@tenantId AND user_id=@userId LIMIT 1;",
                ("@tenantId", tenantId), ("@userId", newOwnerUserId));
            if (await memberCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.NewOwnerNotMember, currentOwnerUserId, newOwnerUserId);
            }

            int updated = await ExecuteAsync(connection, transaction,
                "UPDATE auth_tenant_owners SET user_id=@newOwnerId,assigned_utc=@assigned WHERE tenant_id=@tenantId AND user_id=@currentOwnerId;",
                cancellationToken,
                ("@newOwnerId", newOwnerUserId), ("@assigned", ToUtc(now)), ("@tenantId", tenantId), ("@currentOwnerId", currentOwnerUserId)).ConfigureAwait(false);
            if (updated != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.CurrentOwnerMismatch);
            }

            await ExecuteAsync(connection, transaction,
                "DELETE FROM auth_tenant_user_roles WHERE tenant_id=@tenantId AND user_id=@userId AND role_id=@roleId;",
                cancellationToken,
                ("@tenantId", tenantId), ("@userId", currentOwnerUserId), ("@roleId", TenantOwnerRoleId)).ConfigureAwait(false);
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

            await InvalidateUserSessionsInternalAsync(connection, transaction, currentOwnerUserId, now, cancellationToken).ConfigureAwait(false);
            await InvalidateUserSessionsInternalAsync(connection, transaction, newOwnerUserId, now, cancellationToken).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new TenantOwnershipTransferResult(TenantOwnershipTransferStatus.Success, currentOwnerUserId, newOwnerUserId);
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
    public async Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        bool auditWriteStarted = false;
        try
        {
            int inserted = await InsertTenantMembershipInternalAsync(connection, transaction, tenantId, userId, now, cancellationToken).ConfigureAwait(false);
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
        catch (PostgresException exception) when (!auditWriteStarted && IsConstraintViolation(exception))
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
            : " AND (created_utc < @afterCreated OR (created_utc = @afterCreated AND user_id > @afterId))";
        await using NpgsqlConnection connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        string sql = $"""
            WITH page AS (
                SELECT tenant_id,user_id,created_utc
                FROM auth_tenant_memberships
                WHERE tenant_id=@tenantId
                  {pagePredicate}
                ORDER BY created_utc DESC,user_id ASC
                LIMIT @fetchLimit
            )
            SELECT u.id,u.email,(o.user_id IS NOT NULL),r.name,page.created_utc
            FROM page
            INNER JOIN auth_users u ON u.id=page.user_id
            LEFT JOIN auth_tenant_owners o ON o.tenant_id=page.tenant_id AND o.user_id=page.user_id
            LEFT JOIN auth_tenant_user_roles ur ON ur.user_id=u.id AND ur.tenant_id=page.tenant_id
            LEFT JOIN auth_tenant_roles r ON r.tenant_id=ur.tenant_id AND r.id=ur.role_id
            ORDER BY page.created_utc DESC,page.user_id ASC,r.name;
            """;
        await using NpgsqlCommand command = page.After is null
            ? CreateCommand(connection, null, sql,
                ("@tenantId", tenantId),
                ("@fetchLimit", fetchLimit))
            : CreateCommand(connection, null, sql,
                ("@tenantId", tenantId),
                ("@afterCreated", ToUtc(page.After.CreatedUtc)),
                ("@afterId", page.After.Id),
                ("@fetchLimit", fetchLimit));
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid userId = reader.GetGuid(0);
            if (!members.TryGetValue(userId, out (string Email, bool IsOwner, DateTimeOffset CreatedUtc, List<string> Roles) value))
            {
                value = (reader.GetString(1), reader.GetBoolean(2), ReadDate(reader, 4), []);
                members[userId] = value;
                order.Add(userId);
            }
            if (!reader.IsDBNull(3))
            {
                value.Roles.Add(reader.GetString(3));
            }
        }

        List<(TenantMemberRecord Item, AuthPageBoundary Boundary)> fetched = order
            .Select(userId =>
            {
                var value = members[userId];
                return (new TenantMemberRecord(userId, value.Email, value.IsOwner, value.Roles),
                    new AuthPageBoundary(value.CreatedUtc, userId));
            })
            .ToList();
        return AuthPageSupport.CreateSlice(fetched, pageLimit);
    }

    // Seeds the built-in tenant permission and role catalogs for one new tenant.
    private static async Task SeedTenantAuthorizationCatalogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO auth_tenant_permissions(tenant_id,id,name,description,created_utc) VALUES
                (@tenantId,'30000000-0000-0000-0000-000000000001','tenant.read','Read the active tenant.',@created),
                (@tenantId,'30000000-0000-0000-0000-000000000002','tenant.members.read','Read active-tenant members.',@created),
                (@tenantId,'30000000-0000-0000-0000-000000000003','tenant.members.manage','Manage active-tenant members.',@created),
                (@tenantId,'30000000-0000-0000-0000-000000000004','tenant.roles.manage','Manage active-tenant role assignments.',@created),
                (@tenantId,'30000000-0000-0000-0000-000000000005','tenant.owner.transfer','Transfer active-tenant ownership.',@created);
            INSERT INTO auth_tenant_roles(tenant_id,id,name,normalized_name,description,is_system,created_utc) VALUES
                (@tenantId,'40000000-0000-0000-0000-000000000001','Owner','OWNER','Immutable tenant owner role.',true,@created),
                (@tenantId,'40000000-0000-0000-0000-000000000002','Manager','MANAGER','Manage members and tenant role assignments.',true,@created),
                (@tenantId,'40000000-0000-0000-0000-000000000003','Member','MEMBER','Standard tenant member access.',true,@created);
            INSERT INTO auth_tenant_role_permissions(tenant_id,role_id,permission_id,created_utc) VALUES
                (@tenantId,'40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000002',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000003',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000004',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000005',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000001',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000003',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000004',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000001',@created),
                (@tenantId,'40000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000002',@created);
            """,
            cancellationToken,
            ("@tenantId", tenantId), ("@created", ToUtc(now))).ConfigureAwait(false);
    }

    // Assigns one immutable built-in role to an active tenant member.
    private static Task<int> AssignTenantSystemRoleInternalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO auth_tenant_user_roles(id,tenant_id,user_id,role_id,created_utc)
            SELECT @id,m.tenant_id,m.user_id,r.id,@created
            FROM auth_tenant_memberships m
            INNER JOIN auth_tenant_roles r ON r.tenant_id=m.tenant_id
            WHERE m.tenant_id=@tenantId AND m.user_id=@userId AND r.id=@roleId
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken,
            ("@id", Guid.NewGuid()),
            ("@created", ToUtc(now)),
            ("@tenantId", tenantId),
            ("@userId", userId),
            ("@roleId", roleId));
}
