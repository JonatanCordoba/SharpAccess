using SharpAccess.Domain;

namespace SharpAccess.Services;

internal interface IRegistrationUseCase
{
    // Registers an unverified user and sends a single-use verification link.
    Task<ServiceResult<string>> RegisterAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface IPasswordLoginUseCase
{
    // Authenticates a verified account, applies lockout, and issues a tenant-aware session.
    Task<ServiceResult<SessionTokens>> LoginAsync(
        string? email,
        string? password,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface IRefreshSessionUseCase
{
    // Rotates an opaque refresh token and revokes its family when reuse is detected.
    Task<ServiceResult<SessionTokens>> RefreshAsync(
        string? refreshToken,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Revokes the caller's current refresh token.
    Task<ServiceResult<bool>> LogoutAsync(
        Guid userId,
        string? refreshToken,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Revokes a selected token or family subject to session-management permission.
    Task<ServiceResult<bool>> RevokeAsync(
        Guid userId,
        bool canManageSessions,
        string? refreshToken,
        bool revokeFamily,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface ICurrentUserUseCase
{
    // Loads the authenticated profile and current tenant authorization context.
    Task<ServiceResult<UserContext>> GetMeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

internal interface IPasswordChangeUseCase
{
    // Changes an authenticated user's password and revokes every existing refresh session.
    Task<ServiceResult<bool>> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface IPasswordResetUseCase
{
    // Creates a generic password-reset response and sends a token only for eligible users.
    Task<ServiceResult<string>> ForgotPasswordAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Consumes a single-use reset token, changes the password, and revokes all sessions atomically.
    Task<ServiceResult<bool>> ResetPasswordAsync(
        string? token,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface IEmailVerificationUseCase
{
    // Consumes a single-use verification token and marks the owning email as verified.
    Task<ServiceResult<bool>> VerifyEmailAsync(
        string? token,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Replaces any active verification token and sends a generic response for eligible accounts.
    Task<ServiceResult<string>> ResendVerificationAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}
