using SharpAccess.Abstractions;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;

namespace SharpAccess.Services;

internal interface IAdministrationService
{
    // Lists users with bounded pagination for the administration panel.
    Task<ServiceResult<SharpAccessPage<AuthUser>>> ListUsersAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);

    // Lists global roles through bounded deterministic cursor pagination.
    Task<ServiceResult<SharpAccessPage<RoleRecord>>> ListRolesAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);

    // Creates a dynamic global role after validating its name and description.
    Task<ServiceResult<RoleRecord>> CreateRoleAsync(
        string? name,
        string? description,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Updates a dynamic global role while providers protect immutable system roles.
    Task<ServiceResult<bool>> UpdateRoleAsync(
        Guid roleId,
        string? name,
        string? description,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Lists the provider-backed global permission catalog through cursor pagination.
    Task<ServiceResult<SharpAccessPage<PermissionRecord>>> ListPermissionsAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);

    // Assigns an existing global permission to an existing global role.
    Task<ServiceResult<bool>> AssignPermissionAsync(
        Guid roleId,
        Guid permissionId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Removes an existing global permission from an existing global role.
    Task<ServiceResult<bool>> RemovePermissionAsync(
        Guid roleId,
        Guid permissionId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Assigns a global role to a user.
    Task<ServiceResult<bool>> AssignRoleAsync(
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Removes a global role from a user.
    Task<ServiceResult<bool>> RemoveRoleAsync(
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Activates or deactivates a user and invalidates all sessions by changing security version.
    Task<ServiceResult<bool>> SetUserActiveAsync(
        Guid userId,
        bool isActive,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Lists global audit records with bounded pagination.
    Task<ServiceResult<SharpAccessPage<AuditRecord>>> ListAuditAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class AdministrationService(
    IAuthAdministrationStore store,
    IInputValidator validator,
    IAuthClock clock,
    IAuthPageCursorCodec pageCursorCodec) : IAdministrationService
{
    // Lists users with bounded pagination for the administration panel.
    public async Task<ServiceResult<SharpAccessPage<AuthUser>>> ListUsersAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!pageCursorCodec.TryCreateQuery(
            request,
            AuthPageCursorCodec.UsersScope,
            null,
            out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<AuthUser>>.Failure(AuthError.InvalidInput, "invalid_page");
        }

        AuthPageSlice<AuthUser> page = await store.ListUsersAsync(query, cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<AuthUser>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.UsersScope, null));
    }

    // Lists global roles through a bounded deterministic cursor page.
    public async Task<ServiceResult<SharpAccessPage<RoleRecord>>> ListRolesAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!pageCursorCodec.TryCreateQuery(
            request,
            AuthPageCursorCodec.RolesScope,
            null,
            out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<RoleRecord>>.Failure(AuthError.InvalidInput, "invalid_page");
        }

        AuthPageSlice<RoleRecord> page = await store.ListRolesAsync(query, cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<RoleRecord>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.RolesScope, null));
    }

    // Creates a dynamic global role after validating its name and description.
    public async Task<ServiceResult<RoleRecord>> CreateRoleAsync(
        string? name,
        string? description,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!validator.TryValidateName(name, 100, out string normalized)
            || description is null
            || description.Length > 500)
        {
            return ServiceResult<RoleRecord>.Failure(AuthError.InvalidInput, "invalid_global_role");
        }

        DateTimeOffset now = clock.UtcNow;
        RoleRecord? role = await store.CreateRoleAsync(
            name!.Trim(),
            normalized,
            description.Trim(),
            now,
            SecurityAuditEvidence.Create(
                now,
                "global_role_created",
                actorUserId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null),
            cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return ServiceResult<RoleRecord>.Failure(AuthError.Conflict, "global_role_exists");
        }

        return ServiceResult<RoleRecord>.Success(role);
    }

    // Updates a dynamic global role while providers protect immutable system roles.
    public async Task<ServiceResult<bool>> UpdateRoleAsync(
        Guid roleId,
        string? name,
        string? description,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (roleId == Guid.Empty
            || !validator.TryValidateName(name, 100, out string normalized)
            || description is null
            || description.Length > 500)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_global_role");
        }

        DateTimeOffset now = clock.UtcNow;
        bool updated = await store.UpdateRoleAsync(
            roleId,
            name!.Trim(),
            normalized,
            description.Trim(),
            now,
            SecurityAuditEvidence.Create(
                now,
                "global_role_updated",
                actorUserId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                $"role={roleId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "global_role_not_updated");
        }

        return ServiceResult<bool>.Success(true);
    }

    // Lists global permissions through a bounded deterministic cursor page.
    public async Task<ServiceResult<SharpAccessPage<PermissionRecord>>> ListPermissionsAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!pageCursorCodec.TryCreateQuery(
            request,
            AuthPageCursorCodec.PermissionsScope,
            null,
            out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<PermissionRecord>>.Failure(AuthError.InvalidInput, "invalid_page");
        }

        AuthPageSlice<PermissionRecord> page = await store.ListPermissionsAsync(query, cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<PermissionRecord>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.PermissionsScope, null));
    }

    // Assigns an existing global permission to an existing global role.
    public Task<ServiceResult<bool>> AssignPermissionAsync(
        Guid roleId,
        Guid permissionId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        ChangePermissionAsync(roleId, permissionId, actorUserId, metadata, true, cancellationToken);

    // Removes an existing global permission from an existing global role.
    public Task<ServiceResult<bool>> RemovePermissionAsync(
        Guid roleId,
        Guid permissionId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        ChangePermissionAsync(roleId, permissionId, actorUserId, metadata, false, cancellationToken);

    // Assigns a global role to a user.
    public Task<ServiceResult<bool>> AssignRoleAsync(
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        ChangeGlobalRoleAssignmentAsync(
            userId,
            roleId,
            actorUserId,
            metadata,
            true,
            cancellationToken);

    // Removes a global role from a user.
    public Task<ServiceResult<bool>> RemoveRoleAsync(
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        ChangeGlobalRoleAssignmentAsync(
            userId,
            roleId,
            actorUserId,
            metadata,
            false,
            cancellationToken);

    // Activates or deactivates a user and invalidates all sessions by changing security version.
    public async Task<ServiceResult<bool>> SetUserActiveAsync(
        Guid userId,
        bool isActive,
        Guid actorUserId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_user");
        }

        DateTimeOffset now = clock.UtcNow;
        bool updated = await store.SetUserActiveAsync(
            userId,
            isActive,
            now,
            SecurityAuditEvidence.Create(
                now,
                isActive ? "user_activated" : "user_revoked",
                userId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                $"actor={actorUserId:D}"),
            cancellationToken).ConfigureAwait(false);
        if (!updated)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "user_not_found");
        }

        return ServiceResult<bool>.Success(true);
    }

    // Lists global audit records with bounded pagination.
    public async Task<ServiceResult<SharpAccessPage<AuditRecord>>> ListAuditAsync(
        SharpAccessPageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!pageCursorCodec.TryCreateQuery(
            request,
            AuthPageCursorCodec.AuditScope,
            null,
            out AuthPageQuery query))
        {
            return ServiceResult<SharpAccessPage<AuditRecord>>.Failure(AuthError.InvalidInput, "invalid_page");
        }

        AuthPageSlice<AuditRecord> page = await store.ListAuditAsync(query, cancellationToken).ConfigureAwait(false);
        return ServiceResult<SharpAccessPage<AuditRecord>>.Success(
            pageCursorCodec.CreatePage(page, AuthPageCursorCodec.AuditScope, null));
    }

    // Applies one global role-permission change and writes an audit event.
    private async Task<ServiceResult<bool>> ChangePermissionAsync(
        Guid roleId,
        Guid permissionId,
        Guid actorUserId,
        RequestMetadata metadata,
        bool assign,
        CancellationToken cancellationToken)
    {
        if (roleId == Guid.Empty || permissionId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_global_permission_assignment");
        }

        DateTimeOffset now = clock.UtcNow;
        AuditRecord evidence = SecurityAuditEvidence.Create(
            now,
            "permission_changed",
            actorUserId,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            $"role={roleId:D};permission={permissionId:D};assigned={assign}");
        bool changed = assign
            ? await store.AssignPermissionToRoleAsync(roleId, permissionId, now, evidence, cancellationToken).ConfigureAwait(false)
            : await store.RemovePermissionFromRoleAsync(roleId, permissionId, now, evidence, cancellationToken).ConfigureAwait(false);
        if (!changed)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "global_permission_assignment_not_changed");
        }

        return ServiceResult<bool>.Success(true);
    }

    // Applies one global user-role change.
    private async Task<ServiceResult<bool>> ChangeGlobalRoleAssignmentAsync(
        Guid userId,
        Guid roleId,
        Guid actorUserId,
        RequestMetadata metadata,
        bool assign,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || roleId == Guid.Empty)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_global_role_assignment");
        }

        DateTimeOffset now = clock.UtcNow;
        AuditRecord evidence = SecurityAuditEvidence.Create(
            now,
            assign ? "role_assigned" : "role_removed",
            userId,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            $"role={roleId:D};actor={actorUserId:D}");
        bool changed = assign
            ? await store.AssignGlobalRoleToUserAsync(userId, roleId, now, evidence, cancellationToken).ConfigureAwait(false)
            : await store.RemoveGlobalRoleFromUserAsync(userId, roleId, now, evidence, cancellationToken).ConfigureAwait(false);
        if (!changed)
        {
            return ServiceResult<bool>.Failure(AuthError.NotFound, "global_role_assignment_not_changed");
        }

        return ServiceResult<bool>.Success(true);
    }
}
