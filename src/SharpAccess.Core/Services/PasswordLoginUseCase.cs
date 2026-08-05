using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class PasswordLoginUseCase(
    IAuthUserTenantStore store,
    IPasswordHasher passwordHasher,
    IInputValidator validator,
    IAuditService audit,
    IAuthClock clock,
    IDummyPasswordHashProvider dummyPasswordHash,
    IAuthSessionIssuer sessions,
    IOptions<AuthOptions> options) : IPasswordLoginUseCase
{
    private readonly AuthOptions _options = options.Value;

    // Authenticates a verified account, applies lockout, and issues a tenant-aware session.
    public async Task<ServiceResult<SessionTokens>> LoginAsync(
        string? email,
        string? password,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.PasswordAuthentication)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Disabled, "password_login_disabled");
        }

        if (!TryValidateCredentials(email, password, out string normalizedEmail))
        {
            await VerifyUnknownPasswordAsync(password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        AuthUser? user = await store.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            await VerifyUnknownPasswordAsync(password!, cancellationToken).ConfigureAwait(false);
            await WriteLoginObservationAsync("login_failed", null, tenantId, metadata, cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        PasswordVerificationStatus passwordStatus = await VerifyPasswordAsync(
            user,
            password!,
            cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        bool locked = IsLocked(user, now);
        if (!CanSignIn(user, passwordStatus, locked))
        {
            await HandleRejectedUserAsync(
                user,
                passwordStatus,
                locked,
                tenantId,
                metadata,
                now,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        if (!await HasTenantAccessAsync(user.Id, tenantId, cancellationToken).ConfigureAwait(false))
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        await store.ResetLoginFailuresAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
        await UpgradePasswordHashIfRequiredAsync(
            user,
            password!,
            passwordStatus,
            now,
            cancellationToken).ConfigureAwait(false);

        ServiceResult<SessionTokens> issued = await sessions.IssueSessionAsync(
            user with { FailedLoginAttempts = 0, LockoutEndUtc = null },
            tenantId,
            null,
            metadata,
            cancellationToken).ConfigureAwait(false);
        if (issued.Succeeded)
        {
            await WriteLoginObservationAsync(
                "login_success",
                user.Id,
                tenantId,
                metadata,
                cancellationToken).ConfigureAwait(false);
        }

        return issued;
    }

    private bool TryValidateCredentials(string? email, string? password, out string normalizedEmail)
    {
        normalizedEmail = string.Empty;
        return validator.TryValidateEmail(email, out normalizedEmail)
            && !string.IsNullOrEmpty(password)
            && password.Length <= _options.Passwords.MaximumLength;
    }

    private async Task<PasswordVerificationStatus> VerifyPasswordAsync(
        AuthUser user,
        string password,
        CancellationToken cancellationToken)
    {
        if (user.PasswordHash is null)
        {
            await VerifyUnknownPasswordAsync(password, cancellationToken).ConfigureAwait(false);
            return PasswordVerificationStatus.Failed;
        }

        return await passwordHasher.VerifyAsync(
            password,
            user.PasswordHash,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsLocked(AuthUser user, DateTimeOffset now) =>
        user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > now;

    private static bool CanSignIn(
        AuthUser user,
        PasswordVerificationStatus passwordStatus,
        bool locked) =>
        passwordStatus != PasswordVerificationStatus.Failed
        && user.IsActive
        && user.EmailVerifiedUtc.HasValue
        && !locked;

    private async Task HandleRejectedUserAsync(
        AuthUser user,
        PasswordVerificationStatus passwordStatus,
        bool locked,
        Guid? tenantId,
        RequestMetadata metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (user.IsActive && !locked && passwordStatus == PasswordVerificationStatus.Failed)
        {
            await store.RecordLoginFailureAsync(
                user.Id,
                _options.Lockout.FailedAttempts,
                now.AddMinutes(_options.Lockout.Minutes),
                now,
                cancellationToken).ConfigureAwait(false);
        }

        await WriteLoginObservationAsync(
            "login_failed",
            user.Id,
            tenantId,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasTenantAccessAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            return true;
        }

        return _options.Features.Tenancy
            && await store.IsTenantMemberAsync(userId, tenantId.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpgradePasswordHashIfRequiredAsync(
        AuthUser user,
        string password,
        PasswordVerificationStatus passwordStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (passwordStatus != PasswordVerificationStatus.SuccessNeedsRehash)
        {
            return;
        }

        string upgradedHash = await passwordHasher.HashAsync(password, cancellationToken).ConfigureAwait(false);
        _ = await store.UpdatePasswordHashAsync(
            user.Id,
            user.PasswordHash!,
            user.SecurityVersion,
            upgradedHash,
            now,
            cancellationToken).ConfigureAwait(false);
    }

    private Task WriteLoginObservationAsync(
        string action,
        Guid? userId,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken) =>
        audit.TryWriteObservationAsync(
            action,
            userId,
            tenantId,
            metadata.IpAddress,
            metadata.UserAgent,
            null,
            cancellationToken);

    private static ServiceResult<SessionTokens> InvalidCredentials() =>
        ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_credentials");

    // Performs equivalent Argon2 work for an unknown account to reduce email-enumeration timing differences.
    private async Task VerifyUnknownPasswordAsync(string password, CancellationToken cancellationToken)
    {
        string dummyHash = await dummyPasswordHash.GetAsync(cancellationToken).ConfigureAwait(false);
        string boundedPassword = password.Length <= _options.Passwords.MaximumLength
            ? password
            : password[.._options.Passwords.MaximumLength];
        await passwordHasher.VerifyAsync(boundedPassword, dummyHash, cancellationToken).ConfigureAwait(false);
    }
}
