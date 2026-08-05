using SharpAccess.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpAccess.Configuration;

internal sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    private readonly IAuthClock _clock;
    private readonly IHostEnvironment? _environment;

    // Creates the legacy internal test entry point using the system clock.
    internal AuthOptionsValidator(IHostEnvironment? environment = null)
        : this(new SystemAuthClock(), environment)
    {
    }

    // Creates the validator with an explicit clock for deterministic key-window checks.
    public AuthOptionsValidator(IAuthClock clock, IHostEnvironment? environment = null)
    {
        _clock = clock;
        _environment = environment;
    }

    // Validates feature dependencies, bounded security settings, secrets, and provider trust configuration.
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        _ = name;
        ArgumentNullException.ThrowIfNull(options);
        if (!HasRequiredNestedOptions(options))
        {
            return ValidateOptionsResult.Fail("Nested authentication option objects cannot be null.");
        }

        List<string> failures = [];
        bool isProduction = IsProductionEnvironment();
        ValidationRequirements requirements = CreateValidationRequirements(options);
        Dictionary<string, string> secretFingerprints = new(StringComparer.Ordinal);

        RequireSecureBaseUri(options.BaseUri, nameof(options.BaseUri), failures);
        if (requirements.RequiresJwt)
        {
            ValidateJwtConfiguration(options, isProduction, secretFingerprints, failures);
        }

        ValidateSecurityLimits(options.SecurityLimits, failures);
        if (options.Features.RefreshTokens)
        {
            ValidateRefreshTokenOptions(options, failures);
        }

        ValidateFeatureDurations(options, requirements.HasExternalAuthentication, failures);
        if (requirements.RequiresPasswordSecurity)
        {
            ValidatePasswordSecurityOptions(options.Passwords, isProduction, secretFingerprints, failures);
        }

        if (requirements.RequiresTokenHashing)
        {
            ValidateTokenHashingOptions(options.TokenHashing, isProduction, secretFingerprints, failures);
        }

        if (requirements.HasMappedRateLimitedEndpoint)
        {
            ValidateRateLimitPartitionKey(options, isProduction, failures);
        }

        if (options.Features.PasswordAuthentication)
        {
            ValidateLockoutOptions(options.Lockout, failures);
        }

        ValidateRateLimits(options.Features, options.RateLimits, requirements.HasExternalAuthentication, failures);
        ValidateFeatureDependencies(options.Features, requirements.HasInteractiveSignIn, failures);
        ValidateOpenIdConnectOptions(options.OpenIdConnect, isProduction, secretFingerprints, failures);
        ValidateMigrationOptions(options.Migrations, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // Confirms every nested options object required by validation is present.
    private static bool HasRequiredNestedOptions(AuthOptions options)
    {
        object?[] nestedOptions =
        [
            options.Migrations,
            options.Features,
            options.Passwords,
            options.Passwords?.BreachedPasswords,
            options.TokenHashing,
            options.AccessTokenSigning,
            options.SecurityLimits,
            options.Lockout,
            options.RateLimits,
            options.OpenIdConnect,
            options.OpenIdConnect?.Providers
        ];
        return nestedOptions.All(static value => value is not null);
    }

    // Reports whether predictable or reused secrets must be rejected.
    private bool IsProductionEnvironment() =>
        string.Equals(
            _environment?.EnvironmentName,
            Environments.Production,
            StringComparison.OrdinalIgnoreCase);

    // Derives the feature-dependent validation requirements once per options graph.
    private static ValidationRequirements CreateValidationRequirements(AuthOptions options)
    {
        bool hasExternalAuthentication = options.OpenIdConnect.Providers.Values
            .Any(static provider => provider?.Enabled == true);
        AuthFeatureOptions features = options.Features;
        return new ValidationRequirements(
            hasExternalAuthentication,
            RequiresJwt(features, hasExternalAuthentication),
            RequiresPasswordSecurity(features),
            RequiresTokenHashing(features, hasExternalAuthentication),
            HasMappedRateLimitedEndpoint(features, hasExternalAuthentication),
            features.PasswordAuthentication || hasExternalAuthentication);
    }

    // Reports whether access-token signing configuration is required.
    private static bool RequiresJwt(AuthFeatureOptions features, bool hasExternalAuthentication) =>
        features.PasswordAuthentication
        || features.RefreshTokens
        || hasExternalAuthentication
        || features.Administration
        || features.Tenancy;

    // Reports whether password hashing and breached-password settings are active.
    private static bool RequiresPasswordSecurity(AuthFeatureOptions features) =>
        features.PasswordAuthentication
        || features.Registration
        || features.PasswordReset;

    // Reports whether one-time, refresh, or external-authentication token hashing is active.
    private static bool RequiresTokenHashing(AuthFeatureOptions features, bool hasExternalAuthentication) =>
        features.Registration
        || features.PasswordReset
        || features.RefreshTokens
        || hasExternalAuthentication;

    // Reports whether any mapped endpoint uses the shared partition-key rate limiter.
    private static bool HasMappedRateLimitedEndpoint(
        AuthFeatureOptions features,
        bool hasExternalAuthentication) =>
        features.PasswordAuthentication
        || features.Registration
        || features.PasswordReset
        || features.RefreshTokens
        || hasExternalAuthentication;

    // Validates issuer, audience, signing material, and access-token time bounds.
    private void ValidateJwtConfiguration(
        AuthOptions options,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        RequireText(options.JwtIssuer, nameof(options.JwtIssuer), failures);
        RequireText(options.JwtAudience, nameof(options.JwtAudience), failures);
        ValidateSigningOptions(options, isProduction, secretFingerprints, failures);
        RequireRange(options.AccessTokenMinutes, 1, 1_440, nameof(options.AccessTokenMinutes), failures);
        RequireRange(
            options.FreshAuthenticationMinutes,
            1,
            options.AccessTokenMinutes,
            nameof(options.FreshAuthenticationMinutes),
            failures);
    }

    // Validates refresh-token lifetime, cookie, path, header, and non-loopback requirements.
    private static void ValidateRefreshTokenOptions(AuthOptions options, List<string> failures)
    {
        RequireRange(options.RefreshTokenDays, 1, 365, nameof(options.RefreshTokenDays), failures);
        ValidateCookieName(options.RefreshTokenCookieName, nameof(options.RefreshTokenCookieName), failures);
        ValidateLocalPath(options.RefreshTokenCookiePath, nameof(options.RefreshTokenCookiePath), failures);
        ValidateHeaderName(options.CsrfHeaderName, nameof(options.CsrfHeaderName), failures);
        RequireText(options.CsrfHeaderValue, nameof(options.CsrfHeaderValue), failures);
        if (!IsNonLoopbackAbsoluteHttpUri(options.BaseUri))
        {
            return;
        }

        if (options.RefreshCookieSecurePolicy != Microsoft.AspNetCore.Http.CookieSecurePolicy.Always)
        {
            failures.Add("RefreshCookieSecurePolicy must be Always for non-loopback hosts.");
        }

        if (!options.RequireCsrfHeaderForCookieRefreshRequests)
        {
            failures.Add("RequireCsrfHeaderForCookieRefreshRequests must be enabled for non-loopback hosts.");
        }

        if (!string.IsNullOrWhiteSpace(options.RefreshTokenCookieName)
            && !options.RefreshTokenCookieName.StartsWith("__Secure-", StringComparison.Ordinal))
        {
            failures.Add("RefreshTokenCookieName must use the __Secure- prefix for non-loopback hosts.");
        }
    }

    // Validates feature-specific email, password-reset, and external-authentication durations.
    private static void ValidateFeatureDurations(
        AuthOptions options,
        bool hasExternalAuthentication,
        List<string> failures)
    {
        if (options.Features.Registration)
        {
            RequireRange(options.EmailVerificationMinutes, 5, 10_080, nameof(options.EmailVerificationMinutes), failures);
        }

        if (options.Features.PasswordReset)
        {
            RequireRange(options.PasswordResetMinutes, 5, 1_440, nameof(options.PasswordResetMinutes), failures);
        }

        if (hasExternalAuthentication)
        {
            RequireRange(options.OAuthStateMinutes, 1, 60, nameof(options.OAuthStateMinutes), failures);
            RequireRange(options.OAuthExchangeMinutes, 1, 10, nameof(options.OAuthExchangeMinutes), failures);
        }
    }

    // Validates password hashing bounds, peppers, and breached-password behavior.
    private static void ValidatePasswordSecurityOptions(
        PasswordSecurityOptions passwords,
        bool isProduction,
        Dictionary<string, string> secretFingerprints,
        List<string> failures)
    {
        RequireRange(passwords.MinimumLength, 8, 128, "Passwords.MinimumLength", failures);
        RequireRange(passwords.MaximumLength, passwords.MinimumLength, 1_024, "Passwords.MaximumLength", failures);
        RequireRange(passwords.Iterations, 1, 10, "Passwords.Iterations", failures);
        RequireRange(passwords.MemorySizeKiB, 8_192, 262_144, "Passwords.MemorySizeKiB", failures);
        RequireRange(passwords.DegreeOfParallelism, 1, 32, "Passwords.DegreeOfParallelism", failures);
        RequireRange(passwords.SaltSizeBytes, 16, 64, "Passwords.SaltSizeBytes", failures);
        RequireRange(passwords.HashSizeBytes, 32, 64, "Passwords.HashSizeBytes", failures);
        RequireRange(
            passwords.MaximumConcurrentPasswordHashes,
            1,
            256,
            "Passwords.MaximumConcurrentPasswordHashes",
            failures);
        RequireRange(
            passwords.MaximumQueuedPasswordHashes,
            0,
            10_000,
            "Passwords.MaximumQueuedPasswordHashes",
            failures);
        RequireTimeSpan(
            passwords.PasswordHashQueueTimeout,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMinutes(2),
            "Passwords.PasswordHashQueueTimeout",
            failures);
        RequireText(passwords.CurrentPepperVersion, "Passwords.CurrentPepperVersion", failures);
        ValidatePasswordPeppers(passwords, isProduction, secretFingerprints, failures);
        ValidateBreachedPasswordOptions(passwords.BreachedPasswords, failures);
    }

    // Validates lockout attempt and duration bounds.
    private static void ValidateLockoutOptions(LockoutOptions options, List<string> failures)
    {
        RequireRange(options.FailedAttempts, 1, 100, "Lockout.FailedAttempts", failures);
        RequireRange(options.Minutes, 1, 10_080, "Lockout.Minutes", failures);
    }

    // Validates dependencies between registration, reset, refresh, administration, and tenancy features.
    private static void ValidateFeatureDependencies(
        AuthFeatureOptions features,
        bool hasInteractiveSignIn,
        List<string> failures)
    {
        RequireFeatureDependency(
            features.Registration,
            features.PasswordAuthentication,
            "Registration requires PasswordAuthentication.",
            failures);
        RequireFeatureDependency(
            features.PasswordReset,
            features.PasswordAuthentication,
            "PasswordReset requires PasswordAuthentication.",
            failures);
        RequireFeatureDependency(
            features.RefreshTokens,
            hasInteractiveSignIn,
            "RefreshTokens requires PasswordAuthentication or an enabled OpenIdConnect provider.",
            failures);
        RequireFeatureDependency(
            features.Administration,
            hasInteractiveSignIn,
            "Administration requires PasswordAuthentication or an enabled OpenIdConnect provider.",
            failures);
        RequireFeatureDependency(
            features.Tenancy,
            hasInteractiveSignIn,
            "Tenancy requires PasswordAuthentication or an enabled OpenIdConnect provider.",
            failures);
    }

    // Adds the established failure when an enabled feature's prerequisite is absent.
    private static void RequireFeatureDependency(
        bool enabled,
        bool requirementSatisfied,
        string failure,
        List<string> failures)
    {
        if (enabled && !requirementSatisfied)
        {
            failures.Add(failure);
        }
    }

    private readonly record struct ValidationRequirements(
        bool HasExternalAuthentication,
        bool RequiresJwt,
        bool RequiresPasswordSecurity,
        bool RequiresTokenHashing,
        bool HasMappedRateLimitedEndpoint,
        bool HasInteractiveSignIn);
}
