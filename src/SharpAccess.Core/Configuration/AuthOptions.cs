using SharpAccess;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Configuration;

/// <summary>Configures provider-neutral SharpAccess authentication, token, endpoint, migration, and security behavior.</summary>
public sealed class AuthOptions
{
    /// <summary>Gets or sets the absolute application URI used when SharpAccess creates externally visible authentication links.</summary>
    public Uri BaseUri { get; set; } = new("http://localhost:5000", UriKind.Absolute);
    /// <summary>Gets or sets the issuer that SharpAccess writes to access tokens and requires during validation.</summary>
    public string JwtIssuer { get; set; } = "SharpAccess";
    /// <summary>Gets or sets the audience that SharpAccess writes to access tokens and requires during validation.</summary>
    public string JwtAudience { get; set; } = "SharpAccess.Clients";

    /// <summary>Gets or sets the legacy single HMAC SHA-256 signing secret used when AccessTokenSigning is not configured. New deployments should use the versioned signing-key configuration.</summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    /// <summary>Gets or sets access-token signing and key-rotation configuration.</summary>
    public AccessTokenSigningOptions AccessTokenSigning { get; set; } = new();
    /// <summary>Gets or sets hard limits that bound token size, authorization claims, and refresh-token state.</summary>
    public AuthSecurityLimitOptions SecurityLimits { get; set; } = new();
    /// <summary>Gets or sets the access-token lifetime in minutes.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
    /// <summary>Gets or sets how long a primary credential authentication remains fresh for sensitive operations.</summary>
    public int FreshAuthenticationMinutes { get; set; } = 10;
    /// <summary>Gets or sets the absolute refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 30;
    /// <summary>Gets or sets the lifetime, in minutes, of an email-verification token.</summary>
    public int EmailVerificationMinutes { get; set; } = 60;
    /// <summary>Gets or sets the lifetime, in minutes, of a password-reset token.</summary>
    public int PasswordResetMinutes { get; set; } = 30;
    /// <summary>Gets or sets the lifetime, in minutes, of an OpenID Connect state value.</summary>
    public int OAuthStateMinutes { get; set; } = 10;
    /// <summary>Gets or sets the lifetime, in minutes, of the one-time OpenID Connect callback exchange.</summary>
    public int OAuthExchangeMinutes { get; set; } = 2;
    /// <summary>Gets or sets the name of the HttpOnly refresh-token cookie.</summary>
    public string RefreshTokenCookieName { get; set; } = AuthConstants.DefaultRefreshTokenCookieName;
    /// <summary>Gets or sets the Secure policy applied to the refresh-token cookie.</summary>
    public CookieSecurePolicy RefreshCookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;
    /// <summary>Gets or sets the request path to which the refresh-token cookie is scoped.</summary>
    public string RefreshTokenCookiePath { get; set; } = "/auth";
    /// <summary>Gets or sets whether cookie-backed refresh requests must present the configured CSRF confirmation header.</summary>
    public bool RequireCsrfHeaderForCookieRefreshRequests { get; set; }
    /// <summary>Gets or sets the name of the confirmation header required for protected cookie-backed mutations.</summary>
    public string CsrfHeaderName { get; set; } = AuthConstants.DefaultCsrfHeaderName;
    /// <summary>Gets or sets the exact confirmation-header value accepted for protected cookie-backed mutations.</summary>
    public string CsrfHeaderValue { get; set; } = "1";
    /// <summary>Gets or sets whether refresh tokens are also returned in response bodies instead of remaining cookie-only.</summary>
    public bool ReturnRefreshTokenInResponseBody { get; set; }
    /// <summary>Gets or sets provider-neutral schema migration behavior.</summary>
    public SharpAccessMigrationOptions Migrations { get; set; } = new();
    /// <summary>Gets or sets the authentication and administration feature switches exposed by the endpoint mapper.</summary>
    public AuthFeatureOptions Features { get; set; } = new();
    /// <summary>Gets or sets password policy, Argon2id work factors, pepper rotation, and hashing concurrency limits.</summary>
    public PasswordSecurityOptions Passwords { get; set; } = new();
    /// <summary>Gets or sets versioned keyed hashing used for refresh, verification, reset, and OAuth state tokens.</summary>
    public TokenHashingOptions TokenHashing { get; set; } = new();
    /// <summary>Gets or sets failed-sign-in lockout thresholds.</summary>
    public LockoutOptions Lockout { get; set; } = new();
    /// <summary>Gets or sets per-operation authentication rate limits and privacy-preserving partition-key material.</summary>
    public AuthRateLimitOptions RateLimits { get; set; } = new();
    /// <summary>Gets or sets external OpenID Connect provider registrations.</summary>
    public OpenIdConnectOptions OpenIdConnect { get; set; } = new();

}

/// <summary>Configures access-token signing keys and controlled verification-key rotation.</summary>
public sealed class AccessTokenSigningOptions
{
    /// <summary>Gets or sets whether signing keys are supplied by the host through IAccessTokenSigningKeyRing.</summary>
    public bool UseHostKeyRing { get; set; }
    /// <summary>Gets or sets the identifier of the configured HMAC key used to sign new access tokens.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;
    /// <summary>Gets or sets HMAC SHA-256 keys indexed by JWT key identifier.</summary>
    public IDictionary<string, HmacAccessTokenSigningKeyOptions> HmacSha256Keys { get; set; } =
        new Dictionary<string, HmacAccessTokenSigningKeyOptions>(StringComparer.Ordinal);
}

/// <summary>Configures one HMAC SHA-256 access-token key and its rotation window.</summary>
public sealed class HmacAccessTokenSigningKeyOptions
{
    /// <summary>Gets or sets the secret key material. It must satisfy SharpAccess signing-key strength validation and must not be logged.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Gets or sets the activation timestamp recorded for key ordering and rotation evidence.</summary>
    public DateTimeOffset ActivatedUtc { get; set; } = DateTimeOffset.UnixEpoch;
    /// <summary>Gets or sets the optional earliest UTC instant at which the key may verify tokens.</summary>
    public DateTimeOffset? NotBeforeUtc { get; set; }
    /// <summary>Gets or sets the optional UTC instant after which the key is no longer accepted for verification.</summary>
    public DateTimeOffset? RetiredUtc { get; set; }
}

/// <summary>Configures hard security bounds for authorization claims, encoded tokens, and refresh-token state.</summary>
public sealed class AuthSecurityLimitOptions
{
    /// <summary>Gets or sets the maximum number of role claims emitted in one access token.</summary>
    public int MaximumRolesPerToken { get; set; } = 32;
    /// <summary>Gets or sets the maximum number of permission claims emitted in one access token.</summary>
    public int MaximumPermissionsPerToken { get; set; } = 128;
    /// <summary>Gets or sets the maximum UTF-8 byte length accepted for an encoded access token.</summary>
    public int MaximumEncodedAccessTokenBytes { get; set; } = 8_192;
    /// <summary>Gets or sets the maximum number of simultaneously active refresh-token families for one user.</summary>
    public int MaximumActiveRefreshFamiliesPerUser { get; set; } = 10;
    /// <summary>Gets or sets the maximum number of active refresh tokens retained in one rotation family.</summary>
    public int MaximumActiveRefreshTokensPerFamily { get; set; } = 20;
}

/// <summary>Selects which SharpAccess endpoint capabilities are enabled.</summary>
public sealed class AuthFeatureOptions
{
    /// <summary>Gets or sets whether password sign-in endpoints and password hashing services are enabled.</summary>
    public bool PasswordAuthentication { get; set; }
    /// <summary>Gets or sets whether self-service registration and email verification are enabled.</summary>
    public bool Registration { get; set; }
    /// <summary>Gets or sets whether password-reset request and completion endpoints are enabled.</summary>
    public bool PasswordReset { get; set; }
    /// <summary>Gets or sets whether refresh-token issuance, rotation, and revocation are enabled.</summary>
    public bool RefreshTokens { get; set; }
    /// <summary>Gets or sets whether built-in administration endpoints are mapped.</summary>
    public bool Administration { get; set; }
    /// <summary>Gets or sets whether tenant membership, role, and ownership endpoints are mapped.</summary>
    public bool Tenancy { get; set; }
}

/// <summary>Configures password validation, Argon2id hashing, pepper rotation, and bounded hashing concurrency.</summary>
public sealed class PasswordSecurityOptions
{
    /// <summary>Gets or sets the minimum accepted password length.</summary>
    public int MinimumLength { get; set; } = 15;
    /// <summary>Gets or sets the maximum accepted password length before hashing.</summary>
    public int MaximumLength { get; set; } = 256;
    /// <summary>Gets or sets the Argon2id iteration count.</summary>
    public int Iterations { get; set; } = 3;
    /// <summary>Gets or sets the Argon2id memory cost in kibibytes.</summary>
    public int MemorySizeKiB { get; set; } = 65_536;
    /// <summary>Gets or sets the Argon2id degree of parallelism.</summary>
    public int DegreeOfParallelism { get; set; } = 2;
    /// <summary>Gets or sets the number of random salt bytes generated for each password hash.</summary>
    public int SaltSizeBytes { get; set; } = 16;
    /// <summary>Gets or sets the derived password-hash size in bytes.</summary>
    public int HashSizeBytes { get; set; } = 32;
    /// <summary>Gets or sets the pepper version used when creating new password hashes.</summary>
    public string CurrentPepperVersion { get; set; } = "v1";
    /// <summary>Gets or sets password peppers indexed by immutable version identifier. Pepper material must be supplied from protected host configuration.</summary>
    public IDictionary<string, string> Peppers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Gets or sets the maximum number of password hashes allowed to execute concurrently.</summary>
    public int MaximumConcurrentPasswordHashes { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
    /// <summary>Gets or sets the maximum number of password-hash operations allowed to wait for capacity.</summary>
    public int MaximumQueuedPasswordHashes { get; set; } = 32;
    /// <summary>Gets or sets how long a password-hash operation may wait for bounded hashing capacity.</summary>
    public TimeSpan PasswordHashQueueTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets optional breached-password screening behavior.</summary>
    public BreachedPasswordOptions BreachedPasswords { get; set; } = new();
}

/// <summary>Selects how password validation behaves when the breached-password service cannot produce a trustworthy result.</summary>
public enum BreachedPasswordFailureMode
{
    /// <summary>Allows the password check to continue when the external service is unavailable, while still rejecting confirmed breaches.</summary>
    FailOpen = 0,
    /// <summary>Rejects the password when the external service is unavailable or its result cannot be validated.</summary>
    FailClosed = 1
}

/// <summary>Configures bounded, cacheable, circuit-broken breached-password screening.</summary>
public sealed class BreachedPasswordOptions
{
    /// <summary>Gets or sets whether breached-password screening is enabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the HTTPS k-anonymity range endpoint used for breached-password queries.</summary>
    public Uri Endpoint { get; set; } = new("https://api.pwnedpasswords.com/range/", UriKind.Absolute);
    /// <summary>Gets or sets the maximum duration of one breached-password service request.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Gets or sets the decision applied when the breached-password service is unavailable or invalid.</summary>
    public BreachedPasswordFailureMode FailureMode { get; set; } = BreachedPasswordFailureMode.FailOpen;
    /// <summary>Gets or sets the consecutive failure count that opens the breached-password circuit breaker.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 3;
    /// <summary>Gets or sets how long the breached-password circuit breaker remains open.</summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Gets or sets the maximum number of breached-password prefix results retained in memory.</summary>
    public int MaximumCacheEntries { get; set; } = 2_048;
    /// <summary>Gets or sets the maximum response size accepted from the breached-password service.</summary>
    public int MaximumResponseBytes { get; set; } = 1_048_576;
    /// <summary>Gets or sets how long validated breached-password prefix results are cached.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(12);
}

/// <summary>Configures versioned keyed hashing for persisted opaque authentication tokens.</summary>
public sealed class TokenHashingOptions
{
    /// <summary>Gets or sets the legacy single keyed-hash secret used when Keys is empty. New deployments should use versioned token-hashing keys.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Gets or sets the key version used to hash newly issued opaque tokens.</summary>
    public string CurrentKeyVersion { get; set; } = "v1";
    /// <summary>Gets or sets the key version tried for legacy token hashes that do not carry a version, or null to disable legacy fallback.</summary>
    public string? LegacyUnversionedKeyVersion { get; set; } = "v1";
    /// <summary>Gets or sets keyed-hash secrets indexed by immutable version identifier. Key material must be protected by the host.</summary>
    public IDictionary<string, string> Keys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Configures account lockout after repeated failed password authentication.</summary>
public sealed class LockoutOptions
{
    /// <summary>Gets or sets the failed-attempt count that triggers lockout.</summary>
    public int FailedAttempts { get; set; } = 5;
    /// <summary>Gets or sets the lockout duration in minutes.</summary>
    public int Minutes { get; set; } = 15;
}

/// <summary>Configures per-operation authentication rate limits and their privacy-preserving partition key.</summary>
public sealed class AuthRateLimitOptions
{
    /// <summary>Gets or sets the permitted login attempts per partition per minute.</summary>
    public int LoginPerMinute { get; set; } = 10;
    /// <summary>Gets or sets the permitted registration attempts per partition per minute.</summary>
    public int RegisterPerMinute { get; set; } = 5;
    /// <summary>Gets or sets the permitted refresh attempts per partition per minute.</summary>
    public int RefreshPerMinute { get; set; } = 30;
    /// <summary>Gets or sets the permitted password-reset attempts per partition per minute.</summary>
    public int PasswordResetPerMinute { get; set; } = 5;
    /// <summary>Gets or sets the permitted email-verification attempts per partition per minute.</summary>
    public int EmailVerificationPerMinute { get; set; } = 10;
    /// <summary>Gets or sets the permitted OpenID Connect operations per partition per minute.</summary>
    public int OAuthPerMinute { get; set; } = 20;
    /// <summary>Gets or sets dedicated secret material used to HMAC rate-limit partition dimensions. Supply at least 32 bytes and do not reuse another application secret.</summary>
    public string PartitionKey { get; set; } = string.Empty;
}

/// <summary>Configures named external OpenID Connect providers.</summary>
public sealed class OpenIdConnectOptions
{
    /// <summary>Gets or sets provider definitions indexed by the stable provider name used in endpoint routes.</summary>
    public IDictionary<string, OpenIdConnectProviderOptions> Providers { get; set; } =
        new Dictionary<string, OpenIdConnectProviderOptions>(StringComparer.Ordinal)
        {
            ["google"] = OpenIdConnectProviderOptions.CreateGoogleDefaults()
    };
}

/// <summary>Selects how a confidential OpenID Connect client authenticates to the token endpoint.</summary>
public enum OpenIdConnectClientAuthenticationMethod
{
    /// <summary>Sends the client identifier and secret in the token request body.</summary>
    ClientSecretPost,
    /// <summary>Sends the client identifier and secret through HTTP Basic authentication.</summary>
    ClientSecretBasic
}

/// <summary>Configures one explicitly allowlisted OpenID Connect provider.</summary>
public sealed class OpenIdConnectProviderOptions
{
    /// <summary>Gets or sets whether endpoints for this provider are enabled.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets the registered OpenID Connect client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;
    /// <summary>Gets or sets the confidential client secret. The host must supply and protect this value and must not log it.</summary>
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>Gets or sets how the client authenticates to the provider token endpoint.</summary>
    public OpenIdConnectClientAuthenticationMethod ClientAuthenticationMethod { get; set; } =
        OpenIdConnectClientAuthenticationMethod.ClientSecretPost;
    /// <summary>Gets or sets the local absolute callback path registered with the provider.</summary>
    public string CallbackPath { get; set; } = string.Empty;
    /// <summary>Gets or sets the provider authorization endpoint.</summary>
    public Uri AuthorizationEndpoint { get; set; } = null!;
    /// <summary>Gets or sets the provider token endpoint.</summary>
    public Uri TokenEndpoint { get; set; } = null!;
    /// <summary>Gets or sets the provider JSON Web Key Set endpoint used to validate ID-token signatures.</summary>
    public Uri JsonWebKeySetEndpoint { get; set; } = null!;
    /// <summary>Gets or sets the exact issuer identifiers accepted in validated ID tokens.</summary>
    public IList<string> ValidIssuers { get; set; } = [];
    /// <summary>Gets or sets the OpenID Connect scopes requested during authorization.</summary>
    public IList<string> Scopes { get; set; } = ["openid", "email", "profile"];
    /// <summary>Gets or sets the explicit ID-token signing algorithms accepted from the provider.</summary>
    public IList<string> SigningAlgorithms { get; set; } = ["RS256"];
    /// <summary>Gets or sets the endpoint host allowlist used to prevent untrusted outbound OpenID Connect requests.</summary>
    public IList<string> AllowedHosts { get; set; } = [];
    /// <summary>Gets or sets the optional prompt value sent to the authorization endpoint.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Creates the disabled Google-compatible example entry in the generic provider dictionary.</summary>
    internal static OpenIdConnectProviderOptions CreateGoogleDefaults() => new()
    {
        ClientAuthenticationMethod = OpenIdConnectClientAuthenticationMethod.ClientSecretPost,
        CallbackPath = "/auth/oauth/google/callback",
        AuthorizationEndpoint = new Uri("https://accounts.google.com/o/oauth2/v2/auth", UriKind.Absolute),
        TokenEndpoint = new Uri("https://oauth2.googleapis.com/token", UriKind.Absolute),
        JsonWebKeySetEndpoint = new Uri("https://www.googleapis.com/oauth2/v3/certs", UriKind.Absolute),
        ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
        Scopes = ["openid", "email", "profile"],
        SigningAlgorithms = ["RS256"],
        AllowedHosts = ["accounts.google.com", "oauth2.googleapis.com", "www.googleapis.com"],
        Prompt = "select_account"
    };
}
