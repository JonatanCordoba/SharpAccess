using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns effective authorization-context reads.
internal interface IAuthAuthorizationContextStore
{
    // Gets separately categorized global and active-tenant authorization data.
    Task<EffectiveAuthorizationContext> GetEffectiveAuthorizationContextAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken = default);
}

// Owns global role and permission catalog operations.
internal interface IAuthGlobalAuthorizationStore
{
    // Lists global roles through a validated deterministic keyset page.
    Task<AuthPageSlice<RoleRecord>> ListRolesAsync(AuthPageQuery page, CancellationToken cancellationToken = default);
    // Creates one non-system global role.
    Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<RoleRecord?> CreateRoleAsync(string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        CreateRoleAsync(name, normalizedName, description, now, SecurityAuditEvidence.ForStoreMutation(now, "global_role_created"), cancellationToken);
    // Updates one non-system global role and invalidates affected sessions.
    Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> UpdateRoleAsync(Guid roleId, string name, string normalizedName, string description, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        UpdateRoleAsync(roleId, name, normalizedName, description, now, SecurityAuditEvidence.ForStoreMutation(now, "global_role_updated"), cancellationToken);

    // Lists global permissions through a validated deterministic keyset page.
    Task<AuthPageSlice<PermissionRecord>> ListPermissionsAsync(AuthPageQuery page, CancellationToken cancellationToken = default);

    // Assigns one global permission to a global role.
    Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AssignPermissionToRoleAsync(roleId, permissionId, now, SecurityAuditEvidence.ForStoreMutation(now, "permission_changed"), cancellationToken);
    // Removes one global permission from a global role.
    Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> RemovePermissionFromRoleAsync(Guid roleId, Guid permissionId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RemovePermissionFromRoleAsync(roleId, permissionId, now, SecurityAuditEvidence.ForStoreMutation(now, "permission_changed"), cancellationToken);
    // Assigns one global role to a user.
    Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> AssignGlobalRoleToUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AssignGlobalRoleToUserAsync(userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "role_assigned", userId), cancellationToken);
    // Removes one global role from a user.
    Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> RemoveGlobalRoleFromUserAsync(Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RemoveGlobalRoleFromUserAsync(userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "role_removed", userId), cancellationToken);
}

// Owns tenant-scoped authorization mutations.
internal interface IAuthTenantAuthorizationStore
{
    // Assigns one tenant-scoped role to a tenant member.
    Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> AssignTenantRoleToUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AssignTenantRoleToUserAsync(tenantId, userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_role_assigned", userId, tenantId), cancellationToken);
    // Removes one tenant-scoped role from a tenant member.
    Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> RemoveTenantRoleFromUserAsync(Guid tenantId, Guid userId, Guid roleId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RemoveTenantRoleFromUserAsync(tenantId, userId, roleId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_role_removed", userId, tenantId), cancellationToken);
}

// Composes global, tenant, and effective-context authorization capabilities.
internal interface IAuthAuthorizationStore : IAuthAuthorizationContextStore, IAuthGlobalAuthorizationStore, IAuthTenantAuthorizationStore
{
}
