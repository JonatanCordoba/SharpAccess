using SharpAccess.Abstractions;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;

namespace SharpAccess.Services;

internal interface ITenantService
{
    // Creates a tenant and persists the creator as its immutable initial owner.
    Task<ServiceResult<TenantRecord>> CreateAsync(
        string? name,
        string? slug,
        Guid ownerUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Lists only tenants in which the requesting user has active membership.
    Task<ServiceResult<SharpAccessPage<TenantRecord>>> ListAsync(
        Guid userId,
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);

    // Returns a tenant only to a member or a caller with global tenant-management permission.
    Task<ServiceResult<TenantRecord>> GetAsync(
        Guid tenantId,
        Guid requestingUserId,
        bool canManageAll,
        CancellationToken cancellationToken = default);

    // Gets the current immutable owner record for an accessible tenant.
    Task<ServiceResult<TenantOwnerRecord>> GetOwnerAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    // Transfers ownership atomically to an existing tenant member.
    Task<ServiceResult<TenantOwnerRecord>> TransferOwnershipAsync(
        Guid tenantId,
        Guid newOwnerUserId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Adds a user to a tenant after verifying the actor's membership.
    Task<ServiceResult<bool>> AddMemberAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Lists tenant members only for an existing member.
    Task<ServiceResult<SharpAccessPage<TenantMemberRecord>>> ListMembersAsync(
        Guid tenantId,
        Guid actorUserId,
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);

    // Assigns an existing tenant role after verifying both memberships.
    Task<ServiceResult<bool>> AssignRoleAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal sealed class TenantService(
    IAuthTenantManagementStore store,
    IInputValidator validator,
    IAuthClock clock,
    IAuthPageCursorCodec pageCursorCodec) : ITenantService
{
    // Creates a tenant and persists the creator as its immutable initial owner.
    public async Task<ServiceResult<TenantRecord>> CreateAsync(
        string? name,
        string? slug,
        Guid ownerUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (ownerUserId == Guid.Empty
            || !validator.TryValidateName(name, 150, out _)
            || !validator.TryValidateSlug(slug, out string normalizedSlug))
        {
            return ServiceResult<TenantRecord>.Failure(AuthError.InvalidInput, "invalid_tenant");
        }

        DateTimeOffset now = clock.UtcNow;
        TenantRecord? tenant = await store.CreateTenantAsync(
            name!.Trim(),
            normalizedSlug,
            ownerUserId,
            now,
            SecurityAuditEvidence.Create(
                now,
                "tenant_created",
                ownerUserId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                $"owner={ownerUserId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return ServiceResult<TenantRecord>.Failure(AuthError.Conflict, "tenant_exists");
        }

        return ServiceResult<TenantRecord>.Success(tenant);
    }

    // Lists only tenants in which the requesting user has active membership.
    public async Task<ServiceResult<SharpAccessPage<TenantRecord>>> ListAsync(
        Guid userId,
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty
            || !pageCursorCodec.TryCreateQuery(
                request,
                AuthPageCursorCodec.TenantsScope,
                userId,
                out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<TenantRecord>>.Failure(AuthError.InvalidInput, "invalid_page");
        }

        AuthPageSlice<TenantRecord> page = await store.ListTenantsForUserAsync(
            userId,
            query,
            cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<TenantRecord>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.TenantsScope, userId));
    }

    // Returns a tenant only to a member or a caller with global tenant-management permission.
    public async Task<ServiceResult<TenantRecord>> GetAsync(
        Guid tenantId,
        Guid requestingUserId,
        bool canManageAll,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || requestingUserId == Guid.Empty)
        {
            return ServiceResult<TenantRecord>.Failure(AuthError.InvalidInput, "invalid_tenant");
        }

        if (!canManageAll
            && !await store.IsTenantMemberAsync(requestingUserId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<TenantRecord>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        TenantRecord? tenant = await store.FindTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return tenant is null
            ? ServiceResult<TenantRecord>.Failure(AuthError.NotFound, "tenant_not_found")
            : ServiceResult<TenantRecord>.Success(tenant);
    }

    // Gets the current immutable owner record for an accessible tenant.
    public async Task<ServiceResult<TenantOwnerRecord>> GetOwnerAsync(
        Guid tenantId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return ServiceResult<TenantOwnerRecord>.Failure(AuthError.InvalidInput, "invalid_tenant_owner");
        }

        if (!await store.IsTenantMemberAsync(actorUserId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<TenantOwnerRecord>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        TenantOwnerRecord? owner = await store.GetTenantOwnerAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return owner is null
            ? ServiceResult<TenantOwnerRecord>.Failure(AuthError.NotFound, "tenant_owner_not_found")
            : ServiceResult<TenantOwnerRecord>.Success(owner);
    }

    // Transfers ownership atomically to an existing tenant member.
    public async Task<ServiceResult<TenantOwnerRecord>> TransferOwnershipAsync(
        Guid tenantId,
        Guid newOwnerUserId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || newOwnerUserId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return ServiceResult<TenantOwnerRecord>.Failure(AuthError.InvalidInput, "invalid_tenant_owner_transfer");
        }

        TenantOwnerRecord? currentOwner = await store.GetTenantOwnerAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (currentOwner is null)
        {
            return ServiceResult<TenantOwnerRecord>.Failure(AuthError.NotFound, "tenant_owner_not_found");
        }

        if (currentOwner.UserId != actorUserId)
        {
            return ServiceResult<TenantOwnerRecord>.Failure(AuthError.Forbidden, "tenant_owner_required");
        }

        DateTimeOffset now = clock.UtcNow;
        TenantOwnershipTransferResult transfer = await store.TransferTenantOwnershipAsync(
            tenantId,
            actorUserId,
            newOwnerUserId,
            now,
            SecurityAuditEvidence.Create(
                now,
                "tenant_ownership_transferred",
                newOwnerUserId,
                tenantId,
                metadata.IpAddress,
                metadata.UserAgent,
                $"from={actorUserId:D};to={newOwnerUserId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (transfer.Status != TenantOwnershipTransferStatus.Success)
        {
            AuthError error = transfer.Status switch
            {
                TenantOwnershipTransferStatus.NewOwnerNotMember => AuthError.InvalidInput,
                TenantOwnershipTransferStatus.SameOwner => AuthError.Conflict,
                TenantOwnershipTransferStatus.CurrentOwnerMismatch => AuthError.Forbidden,
                _ => AuthError.NotFound
            };
            return ServiceResult<TenantOwnerRecord>.Failure(error, "tenant_ownership_not_transferred");
        }

        return ServiceResult<TenantOwnerRecord>.Success(new TenantOwnerRecord(tenantId, newOwnerUserId, now));
    }

    // Adds a user to a tenant after verifying the actor's membership.
    public async Task<ServiceResult<bool>> AddMemberAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_tenant_member");
        }

        if (!await store.IsTenantMemberAsync(actorUserId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<bool>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        DateTimeOffset now = clock.UtcNow;
        bool added = await store.AddTenantMemberAsync(
            tenantId,
            userId,
            now,
            SecurityAuditEvidence.Create(
                now,
                "tenant_member_added",
                userId,
                tenantId,
                metadata.IpAddress,
                metadata.UserAgent,
                $"actor={actorUserId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (!added)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "tenant_member_not_added");
        }

        return ServiceResult<bool>.Success(true);
    }

    // Lists tenant members only for an existing member.
    public async Task<ServiceResult<SharpAccessPage<TenantMemberRecord>>> ListMembersAsync(
        Guid tenantId,
        Guid actorUserId,
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty
            || actorUserId == Guid.Empty
            || !pageCursorCodec.TryCreateQuery(
                request,
                AuthPageCursorCodec.TenantMembersScope,
                tenantId,
                out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<TenantMemberRecord>>.Failure(
                AuthError.InvalidInput,
                "invalid_page");
        }

        if (!await store.IsTenantMemberAsync(actorUserId, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<SharpAccessPage<TenantMemberRecord>>.Failure(
                AuthError.Forbidden,
                "tenant_access_denied");
        }

        AuthPageSlice<TenantMemberRecord> page = await store.ListTenantMembersAsync(
            tenantId,
            query,
            cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<TenantMemberRecord>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.TenantMembersScope, tenantId));
    }

    // Assigns an existing tenant role after verifying both memberships.
    public async Task<ServiceResult<bool>> AssignRoleAsync(
        Guid tenantId,
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty
            || userId == Guid.Empty
            || roleId == Guid.Empty
            || actorUserId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_tenant_role");
        }

        bool actorMember = await store.IsTenantMemberAsync(actorUserId, tenantId, cancellationToken).ConfigureAwait(false);
        bool targetMember = await store.IsTenantMemberAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
        if (!actorMember || !targetMember)
        {
            return ServiceResult<bool>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        DateTimeOffset now = clock.UtcNow;
        bool assigned = await store.AssignTenantRoleToUserAsync(
            tenantId,
            userId,
            roleId,
            now,
            SecurityAuditEvidence.Create(
                now,
                "tenant_role_assigned",
                userId,
                tenantId,
                metadata.IpAddress,
                metadata.UserAgent,
                $"role={roleId:D};actor={actorUserId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (!assigned)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "tenant_role_not_assigned");
        }

        return ServiceResult<bool>.Success(true);
    }
}
