
namespace SharpAccess.Domain;

internal sealed record AuthUser(
    Guid Id,
    string Email,
    string NormalizedEmail,
    string? PasswordHash,
    DateTimeOffset? EmailVerifiedUtc,
    bool IsActive,
    int FailedLoginAttempts,
    DateTimeOffset? LockoutEndUtc,
    int SecurityVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

internal sealed record RefreshTokenRecord(
    Guid Id,
    Guid UserId,
    string TokenHash,
    Guid FamilyId,
    int SecurityVersion,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset AuthenticatedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? RevokedUtc,
    Guid? ReplacedByTokenId);

internal sealed record OAuthStateRecord(
    Guid Id,
    string Provider,
    string StateHash,
    string ProtectedCodeVerifier,
    string ReturnUrl,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? ConsumedUtc);

internal sealed record RoleRecord(Guid Id, string Name, string Description, bool IsSystem);

internal sealed record PermissionRecord(Guid Id, string Name, string Description);

internal sealed record TenantRoleRecord(Guid TenantId, Guid Id, string Name, string Description, bool IsSystem);

internal sealed record TenantPermissionRecord(Guid TenantId, Guid Id, string Name, string Description);

internal sealed record TenantRecord(Guid Id, string Name, string Slug, DateTimeOffset CreatedUtc);

internal sealed record TenantOwnerRecord(Guid TenantId, Guid UserId, DateTimeOffset AssignedUtc);

internal sealed record TenantMemberRecord(
    Guid UserId,
    string Email,
    bool IsOwner,
    IReadOnlyList<string> Roles)
{
    // Preserves legacy provider-test construction while treating ownership as an explicit persisted fact.
    internal TenantMemberRecord(Guid userId, string email, IReadOnlyList<string> roles)
        : this(userId, email, false, roles)
    {
    }
}

internal sealed record AuditRecord(
    Guid Id,
    DateTimeOffset CreatedUtc,
    string EventType,
    Guid? UserId,
    Guid? TenantId,
    string? IpAddress,
    string? UserAgent,
    string? Detail);

internal sealed record OAuthIdentity(string Subject, string Email, bool EmailVerified, string? DisplayName);

internal sealed record OneTimeTokenRecord(Guid UserId, string Purpose, DateTimeOffset ExpiresUtc);

internal enum TokenRotationStatus
{
    Success,
    NotFound,
    Expired,
    Reused,
    UserInvalid,
    LimitExceeded
}

internal sealed record TokenRotationResult(TokenRotationStatus Status, Guid? UserId = null, Guid? FamilyId = null);

internal enum TenantOwnershipTransferStatus
{
    Success,
    TenantNotFound,
    CurrentOwnerMismatch,
    NewOwnerNotMember,
    SameOwner
}

internal sealed record TenantOwnershipTransferResult(
    TenantOwnershipTransferStatus Status,
    Guid? PreviousOwnerUserId = null,
    Guid? NewOwnerUserId = null);
