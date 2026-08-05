using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns bounded security audit persistence.
internal interface IAuthAuditStore
{
    // Writes one bounded security audit record.
    Task WriteAuditAsync(AuditRecord audit, CancellationToken cancellationToken = default);

    // Lists audit events through a validated deterministic keyset page.
    Task<AuthPageSlice<AuditRecord>> ListAuditAsync(AuthPageQuery page, CancellationToken cancellationToken = default);
}
