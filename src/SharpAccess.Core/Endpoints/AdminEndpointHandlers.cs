using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Endpoints;

internal static class AdminEndpointHandlers
{
    // Lists users for the administration panel.
    public static async Task<IResult> ListUsersAsync(
        string? cursor,
        int? limit,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<SharpAccessPage<AuthUser>> result = await service.ListUsersAsync(
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        return Results.Ok(new SharpAccessPage<UserResponse>(result.Value.Items.Select(static user => new UserResponse(
            user.Id,
            user.Email,
            user.EmailVerifiedUtc.HasValue,
            user.IsActive,
            user.FailedLoginAttempts,
            user.LockoutEndUtc,
            user.CreatedUtc)).ToArray(), result.Value.NextCursor));
    }

    // Activates or deactivates a user.
    public static async Task<IResult> SetUserStatusAsync(
        Guid userId,
        SetUserStatusRequest request,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.SetUserActiveAsync(
            userId,
            request.IsActive,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Lists global dynamic and system roles.
    public static async Task<IResult> ListRolesAsync(
        string? cursor,
        int? limit,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<SharpAccessPage<RoleRecord>> result = await service.ListRolesAsync(
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        return Results.Ok(new SharpAccessPage<RoleResponse>(result.Value.Items.Select(static role => new RoleResponse(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystem)).ToArray(), result.Value.NextCursor));
    }

    // Creates a dynamic global role.
    public static async Task<IResult> CreateRoleAsync(
        CreateRoleRequest request,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<RoleRecord> result = await service.CreateRoleAsync(
            request.Name,
            request.Description,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? Results.Created($"/admin/roles/{result.Value.Id:D}", new RoleResponse(
                result.Value.Id,
                result.Value.Name,
                result.Value.Description,
                result.Value.IsSystem))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Updates a dynamic global role.
    public static async Task<IResult> UpdateRoleAsync(
        Guid roleId,
        UpdateRoleRequest request,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.UpdateRoleAsync(
            roleId,
            request.Name,
            request.Description,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Lists global permissions.
    public static async Task<IResult> ListPermissionsAsync(
        string? cursor,
        int? limit,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<SharpAccessPage<PermissionRecord>> result = await service.ListPermissionsAsync(
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        return Results.Ok(new SharpAccessPage<PermissionResponse>(result.Value.Items.Select(static permission => new PermissionResponse(
            permission.Id,
            permission.Name,
            permission.Description)).ToArray(), result.Value.NextCursor));
    }

    // Assigns a global permission to a global role.
    public static async Task<IResult> AssignPermissionAsync(
        Guid roleId,
        AssignPermissionRequest request,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.AssignPermissionAsync(
            roleId,
            request.PermissionId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Removes a global permission from a global role.
    public static async Task<IResult> RemovePermissionAsync(
        Guid roleId,
        Guid permissionId,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.RemovePermissionAsync(
            roleId,
            permissionId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Assigns a global role to a user.
    public static async Task<IResult> AssignUserRoleAsync(
        Guid userId,
        AssignRoleRequest request,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.AssignRoleAsync(
            userId,
            request.RoleId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Removes a global role from a user.
    public static async Task<IResult> RemoveUserRoleAsync(
        Guid userId,
        Guid roleId,
        HttpContext httpContext,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid actorUserId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.RemoveRoleAsync(
            userId,
            roleId,
            actorUserId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Lists security audit events.
    public static async Task<IResult> ListAuditAsync(
        string? cursor,
        int? limit,
        IAdministrationService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<SharpAccessPage<AuditRecord>> result = await service.ListAuditAsync(
            new SharpAccessPageRequest(cursor, limit ?? SharpAccessPageRequest.DefaultLimit),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        return Results.Ok(new SharpAccessPage<AuditResponse>(result.Value.Items.Select(static record => new AuditResponse(
            record.Id,
            record.CreatedUtc,
            record.EventType,
            record.UserId,
            record.TenantId,
            record.IpAddress,
            record.UserAgent,
            record.Detail)).ToArray(), result.Value.NextCursor));
    }
}
