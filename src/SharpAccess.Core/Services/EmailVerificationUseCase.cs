using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class EmailVerificationUseCase(
    IAuthUserOneTimeTokenStore store,
    ITokenProtector tokens,
    IInputValidator validator,
    IEmailSender emailSender,
    IAuditService audit,
    IAuthClock clock,
    IOptions<AuthOptions> options) : IEmailVerificationUseCase
{
    private const string VerificationPurpose = "email_verification";
    private readonly AuthOptions _options = options.Value;

    public async Task<ServiceResult<bool>> VerifyEmailAsync(
        string? token,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.Registration)
        {
            return ServiceResult<bool>.Failure(AuthError.Disabled, "registration_disabled");
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > 1_024)
        {
            return ServiceResult<bool>.Failure(AuthError.InvalidInput, "invalid_verification");
        }

        Guid? userId = null;
        DateTimeOffset now = clock.UtcNow;
        AuditRecord evidence = SecurityAuditEvidence.Create(
            now,
            "email_verified",
            null,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            null);
        foreach (string hash in tokens.HashCandidates(token))
        {
            userId = await store.VerifyEmailAsync(hash, now, evidence, cancellationToken).ConfigureAwait(false);
            if (userId.HasValue)
            {
                break;
            }
        }

        if (!userId.HasValue)
        {
            return ServiceResult<bool>.Failure(AuthError.Unauthorized, "invalid_verification");
        }

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<string>> ResendVerificationAsync(
        string? email,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        const string Response = "If the account requires verification, a message has been sent.";
        if (!_options.Features.Registration)
        {
            return ServiceResult<string>.Failure(AuthError.Disabled, "registration_disabled");
        }

        if (!validator.TryValidateEmail(email, out string normalizedEmail))
        {
            return ServiceResult<string>.Success(Response);
        }

        AuthUser? user = await store.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken).ConfigureAwait(false);
        if (user is not null && user.IsActive && !user.EmailVerifiedUtc.HasValue)
        {
            string rawToken = tokens.Generate();
            DateTimeOffset now = clock.UtcNow;
            await store.ReplaceOneTimeTokenAsync(
                user.Id,
                VerificationPurpose,
                tokens.Hash(rawToken),
                now,
                now.AddMinutes(_options.EmailVerificationMinutes),
                cancellationToken).ConfigureAwait(false);
            await SendVerificationEmailAsync(user.Email, rawToken, cancellationToken).ConfigureAwait(false);
            await audit.TryWriteObservationAsync(
                "email_verification_requested",
                user.Id,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                "resend",
                cancellationToken).ConfigureAwait(false);
        }

        return ServiceResult<string>.Success(Response);
    }

    private Task SendVerificationEmailAsync(
        string recipient,
        string token,
        CancellationToken cancellationToken)
    {
        Uri link = BuildClientLink("verify_token", token);
        return emailSender.SendAsync(
            new AuthEmailMessage(
                recipient,
                "Verify your email address",
                $"Open this link to verify your email address: {link}",
                $"<p>Open <a href=\"{link}\">this verification link</a> to verify your email address.</p>"),
            cancellationToken);
    }

    private Uri BuildClientLink(string parameter, string token)
    {
        string fragment = $"{parameter}={Uri.EscapeDataString(token)}";
        UriBuilder builder = new(_options.BaseUri) { Fragment = fragment };
        return builder.Uri;
    }
}
