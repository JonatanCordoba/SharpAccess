using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Endpoints;

internal static class TenantEndpointHandlers
{
    // Creates a tenant owned by the authenticated user.
    public static async Task<IResult> CreateAsync(
        CreateTenantRequest request,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<TenantRecord> result = await service.CreateAsync(
            request.Name,
            request.Slug,
            userId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Created($"/tenants/{result.Value.Id:D}", Map(result.Value))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Lists tenants available to the authenticated user.
    public static async Task<IResult> ListAsync(
        string? cursor,
        int? limit,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<SharpAccessPage<TenantRecord>> result = await service.ListAsync(
            userId,
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        return Results.Ok(new SharpAccessPage<TenantResponse>(
            result.Value.Items.Select(Map).ToArray(),
            result.Value.NextCursor));
    }

    // Gets one tenant when the caller is a member or holds explicit global tenant authority.
    public static async Task<IResult> GetAsync(
        Guid tenantId,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        bool canReadAll = httpContext.User.HasClaim(
                AuthConstants.GlobalPermissionClaim,
                AuthPermissions.TenantsRead)
            || httpContext.User.HasClaim(
                AuthConstants.GlobalPermissionClaim,
                AuthPermissions.TenantsManage);
        ServiceResult<TenantRecord> result = await service.GetAsync(
            tenantId,
            userId,
            canReadAll,
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(Map(result.Value))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Gets the owner of the active tenant.
    public static async Task<IResult> GetOwnerAsync(
        Guid tenantId,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantActor(tenantId, httpContext, out Guid actorUserId, out IResult? failure))
        {
            return failure!;
        }

        ServiceResult<TenantOwnerRecord> result = await service.GetOwnerAsync(
            tenantId,
            actorUserId,
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(Map(result.Value))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Transfers active-tenant ownership to an existing member.
    public static async Task<IResult> TransferOwnershipAsync(
        Guid tenantId,
        TransferTenantOwnershipRequest request,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantActor(tenantId, httpContext, out Guid actorUserId, out IResult? failure))
        {
            return failure!;
        }

        ServiceResult<TenantOwnerRecord> result = await service.TransferOwnershipAsync(
            tenantId,
            request.NewOwnerUserId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(Map(result.Value))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Adds a user to the active tenant.
    public static async Task<IResult> AddMemberAsync(
        Guid tenantId,
        AddTenantMemberRequest request,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantActor(tenantId, httpContext, out Guid actorUserId, out IResult? failure))
        {
            return failure!;
        }

        ServiceResult<bool> result = await service.AddMemberAsync(
            tenantId,
            request.UserId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Lists members of the active tenant.
    public static async Task<IResult> ListMembersAsync(
        Guid tenantId,
        string? cursor,
        int? limit,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantActor(tenantId, httpContext, out Guid actorUserId, out IResult? failure))
        {
            return failure!;
        }

        ServiceResult<SharpAccessPage<TenantMemberRecord>> result = await service.ListMembersAsync(
            tenantId,
            actorUserId,
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Ok(new SharpAccessPage<TenantMemberResponse>(result.Value.Items.Select(static member => new TenantMemberResponse(
                member.UserId,
                member.Email,
                member.IsOwner,
                member.Roles)).ToArray(), result.Value.NextCursor))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Assigns a tenant role to a member inside the active tenant.
    public static async Task<IResult> AssignMemberRoleAsync(
        Guid tenantId,
        Guid userId,
        AssignTenantRoleRequest request,
        HttpContext httpContext,
        ITenantService service,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantActor(tenantId, httpContext, out Guid actorUserId, out IResult? failure))
        {
            return failure!;
        }

        ServiceResult<bool> result = await service.AssignRoleAsync(
            tenantId,
            userId,
            request.RoleId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Verifies that subject and active tenant claims match the requested tenant route.
    private static bool TryGetTenantActor(
        Guid tenantId,
        HttpContext context,
        out Guid actorUserId,
        out IResult? failure)
    {
        actorUserId = Guid.Empty;
        failure = null;
        if (!AuthEndpointUserContext.TryGetUserId(context.User, out actorUserId))
        {
            failure = EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
            return false;
        }

        Guid? activeTenant = AuthEndpointUserContext.TryGetTenantId(context.User);
        if (activeTenant != tenantId)
        {
            failure = EndpointResultFactory.Problem(AuthError.Forbidden, "tenant_context_mismatch");
            return false;
        }

        return true;
    }

    // Maps an internal tenant record to its API response.
    private static TenantResponse Map(TenantRecord tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedUtc);

    // Maps an internal tenant owner record to its API response.
    private static TenantOwnerResponse Map(TenantOwnerRecord owner) =>
        new(owner.TenantId, owner.UserId, owner.AssignedUtc);
}
