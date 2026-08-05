using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns tenant lifecycle, membership, and ownership persistence.
internal interface IAuthTenantStore
{
    // Checks whether a user is an active member of one tenant.
    Task<bool> IsTenantMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
    // Creates a tenant and its initial ownership state atomically.
    Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<TenantRecord?> CreateTenantAsync(string name, string slug, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        CreateTenantAsync(name, slug, ownerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_created", ownerUserId), cancellationToken);

    // Lists one user's tenants through a validated deterministic keyset page.
    Task<AuthPageSlice<TenantRecord>> ListTenantsForUserAsync(Guid userId, AuthPageQuery page, CancellationToken cancellationToken = default);

    // Finds one tenant by identifier.
    Task<TenantRecord?> FindTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    // Gets the immutable owner record for one tenant.
    Task<TenantOwnerRecord?> GetTenantOwnerAsync(Guid tenantId, CancellationToken cancellationToken = default);
    // Transfers tenant ownership atomically to an existing member.
    Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<TenantOwnershipTransferResult> TransferTenantOwnershipAsync(Guid tenantId, Guid currentOwnerUserId, Guid newOwnerUserId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        TransferTenantOwnershipAsync(tenantId, currentOwnerUserId, newOwnerUserId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_ownership_transferred", newOwnerUserId, tenantId), cancellationToken);
    // Adds one active user as a tenant member.
    Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> AddTenantMemberAsync(Guid tenantId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        AddTenantMemberAsync(tenantId, userId, now, SecurityAuditEvidence.ForStoreMutation(now, "tenant_member_added", userId, tenantId), cancellationToken);

    // Lists one tenant's members through a validated deterministic keyset page.
    Task<AuthPageSlice<TenantMemberRecord>> ListTenantMembersAsync(Guid tenantId, AuthPageQuery page, CancellationToken cancellationToken = default);
}
