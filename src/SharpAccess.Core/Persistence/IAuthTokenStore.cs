using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns one-time-token persistence operations.
internal interface IAuthOneTimeTokenStore
{
    // Replaces the active token for one user and purpose atomically.
    Task<bool> ReplaceOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default);
    // Consumes a verification token and marks the user email verified atomically.
    Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<Guid?> VerifyEmailAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        VerifyEmailAsync(tokenHash, now, SecurityAuditEvidence.ForStoreMutation(now, "email_verified"), cancellationToken);
    // Consumes a password-reset token and changes the password atomically.
    Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<Guid?> ResetPasswordAsync(string tokenHash, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ResetPasswordAsync(tokenHash, passwordHash, now, SecurityAuditEvidence.ForStoreMutation(now, "password_reset_completed"), cancellationToken);
    // Creates one purpose-bound one-time token.
    Task<bool> CreateOneTimeTokenAsync(Guid userId, string purpose, string tokenHash, DateTimeOffset createdUtc, DateTimeOffset expiresUtc, CancellationToken cancellationToken = default);
    // Consumes one active purpose-bound token exactly once.
    Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(string purpose, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default);
}

// Owns refresh-token persistence and atomic rotation operations.
internal interface IAuthRefreshTokenStore
{
    // Persists one refresh token.
    Task CreateRefreshTokenAsync(RefreshTokenRecord token, CancellationToken cancellationToken = default);
    // Persists a refresh token only within configured family bounds.
    Task<bool> TryCreateRefreshTokenAsync(RefreshTokenRecord token, int maximumActiveFamiliesPerUser, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default);
    // Finds a refresh token by its keyed hash.
    Task<RefreshTokenRecord?> FindRefreshTokenByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    // Revokes a replayed token family and records the detection atomically.
    Task<bool> HandleRefreshTokenReplayAsync(string tokenHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract replay evidence when request metadata is unavailable.
    Task<bool> HandleRefreshTokenReplayAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        HandleRefreshTokenReplayAsync(tokenHash, now, SecurityAuditEvidence.ForStoreMutation(now, "refresh_token_reuse_detected"), cancellationToken);
    // Rotates one refresh token atomically while enforcing the family bound.
    Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, int maximumActiveTokensPerFamily, RefreshTokenAuditEvidence audit, CancellationToken cancellationToken = default);
    // Creates provider-contract outcome evidence when request metadata is unavailable.
    Task<TokenRotationResult> RotateRefreshTokenAsync(string existingTokenHash, RefreshTokenRecord replacement, DateTimeOffset now, int maximumActiveTokensPerFamily, CancellationToken cancellationToken = default) =>
        RotateRefreshTokenAsync(existingTokenHash, replacement, now, maximumActiveTokensPerFamily, SecurityAuditEvidence.ForRefreshRotation(now, replacement.UserId, familyDetail: $"family={replacement.FamilyId:D}"), cancellationToken);
    // Revokes a token or family after provider-side ownership validation.
    Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> RevokeRefreshTokenAsync(string tokenHash, Guid requestingUserId, bool allowAnyUser, bool revokeFamily, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeRefreshTokenAsync(tokenHash, requestingUserId, allowAnyUser, revokeFamily, now, SecurityAuditEvidence.ForStoreMutation(now, revokeFamily ? "refresh_token_family_revoked" : "refresh_token_revoked", requestingUserId), cancellationToken);
    // Revokes every active token in one family.
    Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<int> RevokeRefreshTokenFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeRefreshTokenFamilyAsync(familyId, now, SecurityAuditEvidence.ForStoreMutation(now, "refresh_token_family_revoked"), cancellationToken);
    // Revokes every active refresh token for one user.
    Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<int> RevokeAllUserRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        RevokeAllUserRefreshTokensAsync(userId, now, SecurityAuditEvidence.ForStoreMutation(now, "user_refresh_tokens_revoked", userId), cancellationToken);
}
