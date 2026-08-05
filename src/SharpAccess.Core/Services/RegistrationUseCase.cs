using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.Extensions.Options;

namespace SharpAccess.Services;

internal sealed class RegistrationUseCase(
    IAuthUserStore store,
    IPasswordHasher passwordHasher,
    ITokenProtector tokens,
    IInputValidator validator,
    IEmailSender emailSender,
    IAuditService audit,
    IAuthClock clock,
    IOptions<AuthOptions> options) : IRegistrationUseCase
{
    private readonly AuthOptions _options = options.Value;

    // Registers an unverified user and sends a single-use verification link.
    public async Task<ServiceResult<string>> RegisterAsync(
        string? email,
        string? password,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Features.Registration)
        {
            return ServiceResult<string>.Failure(AuthError.Disabled, "registration_disabled");
        }

        if (!validator.TryValidateEmail(email, out string normalizedEmail)
            || !validator.IsValidPassword(password))
        {
            return ServiceResult<string>.Failure(AuthError.InvalidInput, "invalid_registration");
        }

        string passwordHash = await passwordHasher.HashAsync(password!, cancellationToken).ConfigureAwait(false);
        string rawVerificationToken = tokens.Generate();
        DateTimeOffset now = clock.UtcNow;
        AuthUser user = new(
            Guid.NewGuid(),
            email!.Trim(),
            normalizedEmail,
            passwordHash,
            null,
            true,
            0,
            null,
            1,
            now,
            now);
        bool created = await store.CreateUserWithVerificationTokenAsync(
            user,
            tokens.Hash(rawVerificationToken),
            now.AddMinutes(_options.EmailVerificationMinutes),
            cancellationToken).ConfigureAwait(false);

        if (created)
        {
            await SendVerificationEmailAsync(user.Email, rawVerificationToken, cancellationToken).ConfigureAwait(false);
            await audit.TryWriteObservationAsync(
                "email_verification_requested",
                user.Id,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        return ServiceResult<string>.Success(
            "If the address can be registered, a verification message has been sent.");
    }

    // Sends the verification link through the host-provided email abstraction.
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

    // Creates an absolute client-side link without placing tokens in server logs.
    private Uri BuildClientLink(string parameter, string token)
    {
        string fragment = $"{parameter}={Uri.EscapeDataString(token)}";
        UriBuilder builder = new(_options.BaseUri) { Fragment = fragment };
        return builder.Uri;
    }
}
