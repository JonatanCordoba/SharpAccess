using System.Globalization;
using SharpAccess.Domain;

namespace SharpAccess.Services;

internal interface IAuthService
{
    // Registers an unverified user and sends a single-use verification link.
    Task<ServiceResult<string>> RegisterAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Authenticates a verified account, applies lockout, and issues a tenant-aware session.
    Task<ServiceResult<SessionTokens>> LoginAsync(
        string? email,
        string? password,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

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

    // Loads the authenticated profile and current tenant authorization context.
    Task<ServiceResult<UserContext>> GetMeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    // Changes an authenticated user's password and revokes every existing refresh session.
    Task<ServiceResult<bool>> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

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

internal sealed record RequestMetadata(string? IpAddress, string? UserAgent);

internal sealed class AuthService(
    IRegistrationUseCase registration,
    IPasswordLoginUseCase login,
    IRefreshSessionUseCase refreshSessions,
    ICurrentUserUseCase currentUser,
    IPasswordChangeUseCase passwordChange,
    IPasswordResetUseCase passwordReset,
    IEmailVerificationUseCase emailVerification) : IAuthService
{
    // Normalizes email addresses for provider compatibility and stable persistence keys.
    internal static string NormalizeEmail(string email) =>
        email.Trim().ToUpper(CultureInfo.InvariantCulture);

    // Registers an account through the focused registration use case.
    public Task<ServiceResult<string>> RegisterAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        registration.RegisterAsync(email, password, metadata, cancellationToken);

    // Authenticates through the focused password-login use case.
    public Task<ServiceResult<SessionTokens>> LoginAsync(
        string? email,
        string? password,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        login.LoginAsync(email, password, tenantId, metadata, cancellationToken);

    // Rotates a refresh token through the focused refresh-session use case.
    public Task<ServiceResult<SessionTokens>> RefreshAsync(
        string? refreshToken,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        refreshSessions.RefreshAsync(refreshToken, tenantId, metadata, cancellationToken);

    // Revokes the caller's current refresh token through the focused refresh-session use case.
    public Task<ServiceResult<bool>> LogoutAsync(
        Guid userId,
        string? refreshToken,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        refreshSessions.LogoutAsync(userId, refreshToken, metadata, cancellationToken);

    // Revokes a selected refresh token through the focused refresh-session use case.
    public Task<ServiceResult<bool>> RevokeAsync(
        Guid userId,
        bool canManageSessions,
        string? refreshToken,
        bool revokeFamily,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        refreshSessions.RevokeAsync(userId, canManageSessions, refreshToken, revokeFamily, metadata, cancellationToken);

    // Loads the authenticated profile through the focused current-user use case.
    public Task<ServiceResult<UserContext>> GetMeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default) =>
        currentUser.GetMeAsync(userId, tenantId, cancellationToken);

    // Changes a password through the focused password-change use case.
    public Task<ServiceResult<bool>> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        passwordChange.ChangePasswordAsync(userId, currentPassword, newPassword, metadata, cancellationToken);

    // Requests a password reset through the focused password-reset use case.
    public Task<ServiceResult<string>> ForgotPasswordAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        passwordReset.ForgotPasswordAsync(email, metadata, cancellationToken);

    // Resets a password through the focused password-reset use case.
    public Task<ServiceResult<bool>> ResetPasswordAsync(
        string? token,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        passwordReset.ResetPasswordAsync(token, newPassword, metadata, cancellationToken);

    // Verifies an email address through the focused email-verification use case.
    public Task<ServiceResult<bool>> VerifyEmailAsync(
        string? token,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        emailVerification.VerifyEmailAsync(token, metadata, cancellationToken);

    // Resends verification through the focused email-verification use case.
    public Task<ServiceResult<string>> ResendVerificationAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        emailVerification.ResendVerificationAsync(email, metadata, cancellationToken);
}
