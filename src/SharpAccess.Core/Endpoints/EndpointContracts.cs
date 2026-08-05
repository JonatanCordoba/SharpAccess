namespace SharpAccess.Endpoints;

internal sealed record RegisterRequest(string? Email, string? Password);
internal sealed record LoginRequest(string? Email, string? Password, Guid? TenantId);
internal sealed record RefreshRequest(string? RefreshToken, Guid? TenantId);
internal sealed record LogoutRequest(string? RefreshToken);
internal sealed record RevokeRequest(string? RefreshToken, bool RevokeFamily);
internal sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
internal sealed record ForgotPasswordRequest(string? Email);
internal sealed record ResetPasswordRequest(string? Token, string? NewPassword);
internal sealed record VerifyEmailRequest(string? Token);
internal sealed record ResendVerificationRequest(string? Email);
internal sealed record OAuthExchangeRequest(string? Code, Guid? TenantId);
internal sealed record CreateRoleRequest(string? Name, string? Description);
internal sealed record UpdateRoleRequest(string? Name, string? Description);
internal sealed record AssignPermissionRequest(Guid PermissionId);
internal sealed record AssignRoleRequest(Guid RoleId);
internal sealed record SetUserStatusRequest(bool IsActive);
internal sealed record CreateTenantRequest(string? Name, string? Slug);
internal sealed record AddTenantMemberRequest(Guid UserId);
internal sealed record AssignTenantRoleRequest(Guid RoleId);
internal sealed record TransferTenantOwnershipRequest(Guid NewOwnerUserId);

internal sealed record MessageResponse(string Message);
internal sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresUtc,
    string TokenType,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresUtc);
internal sealed record MeResponse(
    Guid Id,
    string Email,
    bool EmailVerified,
    IReadOnlyList<string> GlobalRoles,
    IReadOnlyList<string> GlobalPermissions,
    Guid? TenantId,
    bool IsTenantOwner,
    IReadOnlyList<string> TenantRoles,
    IReadOnlyList<string> TenantPermissions,
    long AuthorizationVersion)
{
    // Creates a global-only compatibility response without merging tenant authorization.
    internal MeResponse(
        Guid id,
        string email,
        bool emailVerified,
        IReadOnlyList<string> globalRoles,
        IReadOnlyList<string> globalPermissions,
        Guid? tenantId)
        : this(id, email, emailVerified, globalRoles, globalPermissions, tenantId, false, [], [], 0)
    {
    }
}
internal sealed record UserResponse(
    Guid Id,
    string Email,
    bool EmailVerified,
    bool IsActive,
    int FailedLoginAttempts,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset CreatedUtc);
internal sealed record RoleResponse(Guid Id, string Name, string Description, bool IsSystem);
internal sealed record PermissionResponse(Guid Id, string Name, string Description);
internal sealed record TenantResponse(Guid Id, string Name, string Slug, DateTimeOffset CreatedUtc);
internal sealed record TenantOwnerResponse(Guid TenantId, Guid UserId, DateTimeOffset AssignedUtc);
internal sealed record TenantMemberResponse(
    Guid UserId,
    string Email,
    bool IsOwner,
    IReadOnlyList<string> Roles)
{
    // Creates a non-owner compatibility response for callers that predate explicit ownership.
    internal TenantMemberResponse(Guid userId, string email, IReadOnlyList<string> roles)
        : this(userId, email, false, roles)
    {
    }
}
internal sealed record AuditResponse(
    Guid Id,
    DateTimeOffset CreatedUtc,
    string EventType,
    Guid? UserId,
    Guid? TenantId,
    string? IpAddress,
    string? UserAgent,
    string? Detail);
