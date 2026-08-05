using SharpAccess.Configuration;
using SharpAccess.Domain;

namespace SharpAccess.Persistence;

// Owns user-account persistence operations.
internal interface IAuthUserStore
{
    // Creates a user and initial verification token atomically.
    Task<bool> CreateUserWithVerificationTokenAsync(AuthUser user, string verificationTokenHash, DateTimeOffset verificationExpiresUtc, CancellationToken cancellationToken = default);
    // Finds a user by normalized email.
    Task<AuthUser?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    // Finds a user by identifier.
    Task<AuthUser?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    // Lists users through a validated deterministic keyset page.
    Task<AuthPageSlice<AuthUser>> ListUsersAsync(AuthPageQuery page, CancellationToken cancellationToken = default);

    // Records one failed login and applies the provider-neutral lockout threshold.
    Task RecordLoginFailureAsync(Guid userId, int failureThreshold, DateTimeOffset lockoutEndUtc, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default);
    // Clears persisted failed-login state.
    Task ResetLoginFailuresAsync(Guid userId, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default);
    // Replaces a password hash only when the expected security state still matches.
    Task<bool> UpdatePasswordHashAsync(Guid userId, string expectedPasswordHash, int expectedSecurityVersion, string passwordHash, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default);
    // Changes a password and invalidates existing sessions atomically.
    Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ChangePasswordAsync(userId, passwordHash, now, SecurityAuditEvidence.ForStoreMutation(now, "password_changed", userId), cancellationToken);
    // Activates or deactivates a user and invalidates existing sessions.
    Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        SetUserActiveAsync(userId, isActive, now, SecurityAuditEvidence.ForStoreMutation(now, isActive ? "user_activated" : "user_revoked", userId), cancellationToken);
}

// Owns deterministic administrator seeding.
internal interface IAuthAdminSeedStore
{
    // Seeds or updates the configured administrator with atomic audit evidence.
    Task SeedAdminAsync(AdminSeedOptions options, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default);
    // Creates provider-contract audit evidence when request metadata is unavailable.
    Task SeedAdminAsync(AdminSeedOptions options, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        SeedAdminAsync(options, passwordHash, now, SecurityAuditEvidence.ForStoreMutation(now, "administrator_seeded"), cancellationToken);
}
