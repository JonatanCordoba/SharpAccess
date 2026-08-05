using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class PasswordChangeUseCase(
    IAuthUserStore store,
    IPasswordHasher passwordHasher,
    IInputValidator validator,
    IAuthClock clock,
    IOptions<AuthOptions> options) : IPasswordChangeUseCase
{
    private readonly AuthOptions _options = options.Value;

    // Changes an authenticated user's password and revokes every existing refresh session.
    public async Task<ServiceResult<bool>> ChangePasswordAsync(
        Guid userId,
        string? currentPassword,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.PasswordAuthentication
            || userId == Guid.Empty
            || !validator.IsValidPassword(newPassword)
            || string.IsNullOrEmpty(currentPassword)
            || currentPassword.Length > _options.Passwords.MaximumLength)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_password_change");
        }

        AuthUser? user = await store.FindUserByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive || !user.EmailVerifiedUtc.HasValue || user.PasswordHash is null)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_password_change");
        }

        PasswordVerificationStatus verified = await passwordHasher.VerifyAsync(
            currentPassword,
            user.PasswordHash,
            cancellationToken).ConfigureAwait(false);
        if (verified == PasswordVerificationStatus.Failed)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_password_change");
        }

        string hash = await passwordHasher.HashAsync(newPassword!, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        bool changed = await store.ChangePasswordAsync(
            userId,
            hash,
            now,
            SecurityAuditEvidence.Create(
                now,
                "password_changed",
                userId,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null),
            cancellationToken).ConfigureAwait(false);
        if (!changed)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_password_change");
        }

        return ServiceResult<bool>.Success(true);
    }
}
