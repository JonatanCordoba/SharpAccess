using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class PasswordResetUseCase(
    IAuthUserOneTimeTokenStore store,
    IPasswordHasher passwordHasher,
    ITokenProtector tokens,
    IInputValidator validator,
    IEmailSender emailSender,
    IAuditService audit,
    IAuthClock clock,
    IOptions<AuthOptions> options) : IPasswordResetUseCase
{
    private const string PasswordResetPurpose = "password_reset";
    private readonly AuthOptions _options = options.Value;

    public async Task<ServiceResult<string>> ForgotPasswordAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        const string Response = "If the account is eligible, a password reset message has been sent.";
        if (!_options.Features.PasswordReset)
        {
            return ServiceResult<string>.Failure(AuthError.Disabled, "password_reset_disabled");
        }

        if (!validator.TryValidateEmail(email, out string normalizedEmail))
        {
            return ServiceResult<string>.Success(Response);
        }

        AuthUser? user = await store.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (user is not null && user.IsActive && user.EmailVerifiedUtc.HasValue)
        {
            string rawToken = tokens.Generate();
            DateTimeOffset now = clock.UtcNow;
            await store.ReplaceOneTimeTokenAsync(
                user.Id,
                PasswordResetPurpose,
                tokens.Hash(rawToken),
                now,
                now.AddMinutes(_options.PasswordResetMinutes),
                cancellationToken).ConfigureAwait(false);
            await SendPasswordResetEmailAsync(user.Email, rawToken, cancellationToken).ConfigureAwait(false);
            await audit.TryWriteObservationAsync(
                "password_reset_requested",
                user.Id,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        return ServiceResult<string>.Success(Response);
    }

    public async Task<ServiceResult<bool>> ResetPasswordAsync(
        string? token,
        string? newPassword,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.PasswordReset
            || string.IsNullOrWhiteSpace(token)
            || token.Length > 1_024
            || !validator.IsValidPassword(newPassword))
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_password_reset");
        }

        string hash = await passwordHasher.HashAsync(newPassword!, cancellationToken).ConfigureAwait(false);
        Guid? userId = null;
        DateTimeOffset now = clock.UtcNow;
        AuditRecord evidence = SecurityAuditEvidence.Create(
            now,
            "password_reset_completed",
            null,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            null);
        foreach (string candidate in tokens.HashCandidates(token))
        {
            userId = await store.ResetPasswordAsync(
                candidate,
                hash,
                now,
                evidence,
                cancellationToken).ConfigureAwait(false);
            if (userId.HasValue)
            {
                break;
            }
        }

        if (!userId.HasValue)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_password_reset");
        }

        return ServiceResult<bool>.Success(true);
    }

    private Task SendPasswordResetEmailAsync(
        string recipient,
        string token,
        CancellationToken cancellationToken)
    {
        Uri link = BuildClientLink("reset_token", token);
        return emailSender.SendAsync(
            new AuthEmailMessage(
                recipient,
                "Reset your password",
                $"Open this link to reset your password: {link}",
                $"<p>Open <a href=\"{link}\">this password reset link</a> to choose a new password.</p>"),
            cancellationToken);
    }

    private Uri BuildClientLink(string parameter, string token)
    {
        string fragment = $"{parameter}={Uri.EscapeDataString(token)}";
        UriBuilder builder = new(_options.BaseUri) { Fragment = fragment };
        return builder.Uri;
    }
}
