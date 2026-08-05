using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns protected external-authentication state and identity persistence.
internal interface IAuthOAuthStore
{
    // Persists protected external-authentication state.
    Task SaveOAuthStateAsync(OAuthStateRecord state, CancellationToken cancellationToken = default);
    // Consumes unexpired external-authentication state exactly once.
    Task<OAuthStateRecord?> ConsumeOAuthStateAsync(string provider, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default);
    // Resolves or creates the local user bound to an external subject with atomic binding evidence.
    Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract binding evidence when request metadata is unavailable.
    Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(string provider, string providerSubject, string email, string normalizedEmail, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ResolveOAuthUserAsync(
            provider,
            providerSubject,
            email,
            normalizedEmail,
            now,
            SecurityAuditEvidence.Create(now, "oauth_account_linked", null, null, null, null, $"provider={provider}"),
            cancellationToken);
}
