using SharpAccess.Diagnostics;
using SharpAccess.Domain;
using SharpAccess.Security;

namespace SharpAccess.Services;

internal sealed class PasswordRiskAuthService(
    IRegistrationUseCase registration,
    IPasswordLoginUseCase login,
    IRefreshSessionUseCase refreshSessions,
    ICurrentUserUseCase currentUser,
    IPasswordChangeUseCase passwordChange,
    IPasswordResetUseCase passwordReset,
    IEmailVerificationUseCase emailVerification,
    IInputValidator inputValidator,
    IPasswordRiskValidator passwordRiskValidator) : IAuthService
{
    // Registers an account and records one bounded diagnostic operation.
    public Task<ServiceResult<string>> RegisterAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Register,
            () => RegisterCoreAsync(email, password, metadata, cancellationToken));

    // Authenticates a verified account and records one bounded diagnostic operation.
    public Task<ServiceResult<SessionTokens>> LoginAsync(
        string? email,
        string? password,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Login,
            () => login.LoginAsync(email, password, tenantId, metadata, cancellationToken));

    // Rotates a refresh token and records one bounded diagnostic operation.
    public Task<ServiceResult<SessionTokens>> RefreshAsync(
        string? refreshToken,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Refresh,
            () => refreshSessions.RefreshAsync(refreshToken, tenantId, metadata, cancellationToken));

    // Revokes the current refresh token and records one bounded diagnostic operation.
    public Task<ServiceResult<bool>> LogoutAsync(
        Guid userId,
        string? refreshToken,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Logout,
            () => refreshSessions.LogoutAsync(userId, refreshToken, metadata, cancellationToken));

    // Revokes a selected refresh token and records one bounded diagnostic operation.
    public Task<ServiceResult<bool>> RevokeAsync(
        Guid userId,
        bool canManageSessions,
        string? refreshToken,
        bool revokeFamily,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Revoke,
            () => refreshSessions.RevokeAsync(
                userId,
                canManageSessions,
                refreshToken,
                revokeFamily,
                metadata,
                cancellationToken));

    // Loads the authenticated profile and records one bounded diagnostic operation.
    public Task<ServiceResult<UserContext>> GetMeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.CurrentUser,
            () => currentUser.GetMeAsync(userId, tenantId, cancellationToken));

    // Changes a password and records one bounded diagnostic operation.
    public Task<ServiceResult<bool>> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.ChangePassword,
            () => ChangePasswordCoreAsync(
                userId,
                currentPassword,
                newPassword,
                metadata,
                cancellationToken));

    // Requests a password reset and records one bounded diagnostic operation.
    public Task<ServiceResult<string>> ForgotPasswordAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.ForgotPassword,
            () => passwordReset.ForgotPasswordAsync(email, metadata, cancellationToken));

    // Resets a password and records one bounded diagnostic operation.
    public Task<ServiceResult<bool>> ResetPasswordAsync(
        string? token,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.ResetPassword,
            () => ResetPasswordCoreAsync(token, newPassword, metadata, cancellationToken));

    // Verifies an email address and records one bounded diagnostic operation.
    public Task<ServiceResult<bool>> VerifyEmailAsync(
        string? token,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.VerifyEmail,
            () => emailVerification.VerifyEmailAsync(token, metadata, cancellationToken));

    // Resends verification and records one bounded diagnostic operation.
    public Task<ServiceResult<string>> ResendVerificationAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default) =>
        SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.ResendVerification,
            () => emailVerification.ResendVerificationAsync(email, metadata, cancellationToken));

    // Applies syntactic and host-provided password-risk validation before registration.
    private async Task<ServiceResult<string>> RegisterCoreAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!inputValidator.TryValidateEmail(email, out string normalizedEmail)
            || !inputValidator.IsValidPassword(password)
            || !await passwordRiskValidator.IsAllowedAsync(
                password!,
                normalizedEmail,
                cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<string>.Failure(
                AuthError.InvalidInput,
                "invalid_registration");
        }

        return await registration.RegisterAsync(
            email,
            password,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    // Applies host-provided password-risk validation before changing a password.
    private async Task<ServiceResult<bool>> ChangePasswordCoreAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!inputValidator.IsValidPassword(newPassword)
            || !await passwordRiskValidator.IsAllowedAsync(
                newPassword!,
                null,
                cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<bool>.Failure(
                AuthError.InvalidInput,
                "invalid_password_change");
        }

        return await passwordChange.ChangePasswordAsync(
            userId,
            currentPassword,
            newPassword,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    // Applies host-provided password-risk validation before resetting a password.
    private async Task<ServiceResult<bool>> ResetPasswordCoreAsync(
        string? token,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!inputValidator.IsValidPassword(newPassword)
            || !await passwordRiskValidator.IsAllowedAsync(
                newPassword!,
                null,
                cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<bool>.Failure(
                AuthError.InvalidInput,
                "invalid_password_reset");
        }

        return await passwordReset.ResetPasswordAsync(
            token,
            newPassword,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }
}
