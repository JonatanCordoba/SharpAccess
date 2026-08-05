using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.OAuth;

internal interface IOAuthService
{
    // Creates an authorization-code challenge with PKCE S256, state, and OpenID Connect nonce.
    Task<ServiceResult<Uri>> CreateChallengeAsync(
        string provider,
        string? returnUrl,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Consumes state, validates the provider identity, and redirects with a one-time local exchange code.
    Task<ServiceResult<Uri>> HandleCallbackAsync(
        string provider,
        string? code,
        string? state,
        string? error,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);

    // Exchanges the short-lived local result code for a package access and refresh session.
    Task<ServiceResult<SessionTokens>> ExchangeAsync(
        string provider,
        string? exchangeCode,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default);
}

internal interface IExternalOAuthProvider
{
    // Reports whether the keyed provider is configured and enabled.
    bool IsEnabled(string provider);

    // Creates a configured authorization URI for Authorization Code with PKCE S256.
    Uri CreateAuthorizationUri(string provider, string state, string codeChallenge, string nonce);

    // Exchanges the authorization code and validates the returned OpenID Connect identity token.
    Task<OAuthProviderIdentity?> ExchangeAndValidateAsync(
        string provider,
        string code,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken = default);
}

internal sealed record OAuthProviderIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? DisplayName);

internal sealed class ExternalOAuthProviderException : Exception
{
    // Creates a sanitized exception for transport or untrusted provider-payload failures.
    public ExternalOAuthProviderException()
        : base("The external identity provider response could not be processed.")
    {
    }
}

internal static class OAuthAuditWriter
{
    // Writes one bounded external-authentication failure without raw provider credentials or protocol values.
    internal static Task WriteFailureAsync(
        IAuditService audit,
        string provider,
        RequestMetadata metadata,
        string reason,
        CancellationToken cancellationToken) =>
        audit.TryWriteObservationAsync(
            "oauth_login_failed",
            null,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            $"provider={provider};reason={reason}",
            cancellationToken);
}

internal sealed class OAuthService : IOAuthService
{
    private const string ExchangePurposePrefix = "oauth_exchange:";
    private const int MaximumProviderSubjectLength = 256;
    private const int MaximumProviderEmailLength = 320;
    private const int MaximumProviderDisplayNameLength = 200;
    private readonly IReadOnlyList<IExternalOAuthProvider> _providers;
    private readonly IAuthOAuthPersistenceStore _store;
    private readonly ITokenProtector _tokens;
    private readonly IInputValidator _validator;
    private readonly IDataProtector _dataProtector;
    private readonly IAuthClock _clock;
    private readonly IAuditService _audit;
    private readonly IAuthSessionIssuer _sessionIssuer;
    private readonly AuthOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    // Creates the provider-neutral OAuth 2.1 orchestration service.
    public OAuthService(
        IEnumerable<IExternalOAuthProvider> providers,
        IAuthOAuthPersistenceStore store,
        ITokenProtector tokens,
        IInputValidator validator,
        IDataProtectionProvider dataProtectionProvider,
        IAuthClock clock,
        IAuditService audit,
        IAuthSessionIssuer sessionIssuer,
        IOptions<AuthOptions> options)
    {
        _providers = providers.ToArray();
        _store = store;
        _tokens = tokens;
        _validator = validator;
        _dataProtector = dataProtectionProvider.CreateProtector("SharpAccess.OAuth.State.v1");
        _clock = clock;
        _audit = audit;
        _sessionIssuer = sessionIssuer;
        _options = options.Value;
    }

    // Creates an authorization-code challenge with PKCE S256, state, and OpenID Connect nonce.
    public async Task<ServiceResult<Uri>> CreateChallengeAsync(
        string provider,
        string? returnUrl,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        IExternalOAuthProvider? externalProvider = FindEnabledProvider(provider);
        if (externalProvider is null)
        {
            return ServiceResult<Uri>.Failure(AuthError.Disabled, "oauth_provider_disabled");
        }

        if (!_validator.TryValidateReturnUrl(returnUrl, out string safeReturnUrl))
        {
            return ServiceResult<Uri>.Failure(AuthError.InvalidInput, "invalid_return_url");
        }

        string state = _tokens.Generate(32);
        string codeVerifier = _tokens.Generate(64);
        string nonce = _tokens.Generate(32);
        string protectedPayload = _dataProtector.Protect(JsonSerializer.Serialize(
            new OAuthProtectedPayload(codeVerifier, nonce),
            _jsonOptions));
        DateTimeOffset now = _clock.UtcNow;
        await _store.SaveOAuthStateAsync(
            new OAuthStateRecord(
                Guid.NewGuid(),
                provider,
                _tokens.Hash(state),
                protectedPayload,
                safeReturnUrl,
                now,
                now.AddMinutes(_options.OAuthStateMinutes),
                null),
            cancellationToken).ConfigureAwait(false);

        string codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return ServiceResult<Uri>.Success(externalProvider.CreateAuthorizationUri(provider, state, codeChallenge, nonce));
    }

    // Consumes state, validates the provider identity, and redirects with a one-time local exchange code.
    public async Task<ServiceResult<Uri>> HandleCallbackAsync(
        string provider,
        string? code,
        string? state,
        string? error,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        IExternalOAuthProvider? externalProvider = FindEnabledProvider(provider);
        if (externalProvider is null)
        {
            return ServiceResult<Uri>.Failure(AuthError.Disabled, "oauth_provider_disabled");
        }

        if (!IsValidCallbackState(state))
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "invalid_state",
                cancellationToken).ConfigureAwait(false);
        }

        OAuthStateRecord? storedState = await ConsumeOAuthStateAsync(
            provider,
            state!,
            cancellationToken).ConfigureAwait(false);
        if (storedState is null)
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "invalid_state",
                cancellationToken).ConfigureAwait(false);
        }

        if (!IsValidAuthorizationResponse(code, error))
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "authorization_rejected",
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryReadProtectedPayload(storedState, out OAuthProtectedPayload? payload))
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "invalid_state_payload",
                cancellationToken).ConfigureAwait(false);
        }
        OAuthProtectedPayload validPayload = payload!;

        OAuthProviderIdentity? identity;
        try
        {
            identity = await externalProvider.ExchangeAndValidateAsync(
                provider,
                code!,
                validPayload.CodeVerifier,
                validPayload.Nonce,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalOAuthProviderException)
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "provider_failure",
                cancellationToken,
                AuthError.ExternalProviderFailure,
                "oauth_provider_failed").ConfigureAwait(false);
        }

        if (!TryValidateProviderIdentity(identity, out string normalizedEmail))
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "invalid_identity",
                cancellationToken).ConfigureAwait(false);
        }

        ServiceResult<AuthUser> resolved = await ResolveOAuthUserAsync(
            provider,
            identity!,
            normalizedEmail,
            metadata,
            cancellationToken).ConfigureAwait(false);
        if (!resolved.Succeeded || resolved.Value is null)
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "unsafe_account_link",
                cancellationToken,
                AuthError.Conflict,
                "oauth_account_conflict").ConfigureAwait(false);
        }

        return await CreateExchangeRedirectAsync(
            provider,
            storedState,
            resolved.Value,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValidCallbackState(string? state) =>
        !string.IsNullOrWhiteSpace(state) && state.Length <= 1_024;

    private async Task<OAuthStateRecord?> ConsumeOAuthStateAsync(
        string provider,
        string state,
        CancellationToken cancellationToken)
    {
        foreach (string stateHash in _tokens.HashCandidates(state))
        {
            OAuthStateRecord? storedState = await _store.ConsumeOAuthStateAsync(
                provider,
                stateHash,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (storedState is not null)
            {
                return storedState;
            }
        }

        return null;
    }

    private static bool IsValidAuthorizationResponse(string? code, string? error) =>
        string.IsNullOrWhiteSpace(error)
        && !string.IsNullOrWhiteSpace(code)
        && code.Length <= 4_096;

    private bool TryReadProtectedPayload(
        OAuthStateRecord storedState,
        out OAuthProtectedPayload? payload)
    {
        try
        {
            string json = _dataProtector.Unprotect(storedState.ProtectedCodeVerifier);
            payload = JsonSerializer.Deserialize<OAuthProtectedPayload>(json, _jsonOptions);
            return payload is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            payload = null;
            return false;
        }
    }

    private bool TryValidateProviderIdentity(
        OAuthProviderIdentity? identity,
        out string normalizedEmail)
    {
        normalizedEmail = string.Empty;
        return identity is not null
            && HasBoundedIdentityClaims(identity)
            && identity.EmailVerified
            && _validator.TryValidateEmail(identity.Email, out normalizedEmail);
    }

    private Task<ServiceResult<AuthUser>> ResolveOAuthUserAsync(
        string provider,
        OAuthProviderIdentity identity,
        string normalizedEmail,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        return _store.ResolveOAuthUserAsync(
            provider,
            identity.Subject,
            identity.Email,
            normalizedEmail,
            now,
            SecurityAuditEvidence.Create(
                now,
                "oauth_account_linked",
                null,
                null,
                metadata.IpAddress,
                metadata.UserAgent,
                $"provider={provider}"),
            cancellationToken);
    }

    private async Task<ServiceResult<Uri>> CreateExchangeRedirectAsync(
        string provider,
        OAuthStateRecord storedState,
        AuthUser user,
        RequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        string exchangeCode = _tokens.Generate(48);
        DateTimeOffset now = _clock.UtcNow;
        bool exchangeCreated = await _store.CreateOneTimeTokenAsync(
            user.Id,
            ExchangePurpose(provider),
            _tokens.Hash(exchangeCode),
            now,
            now.AddMinutes(_options.OAuthExchangeMinutes),
            cancellationToken).ConfigureAwait(false);
        if (!exchangeCreated)
        {
            return await FailCallbackAsync(
                provider,
                metadata,
                "exchange_code_conflict",
                cancellationToken,
                AuthError.ExternalProviderFailure,
                "oauth_exchange_failed").ConfigureAwait(false);
        }

        await _audit.TryWriteObservationAsync(
            "oauth_login_success",
            user.Id,
            null,
            metadata.IpAddress,
            metadata.UserAgent,
            provider,
            cancellationToken).ConfigureAwait(false);

        return ServiceResult<Uri>.Success(AppendFragment(storedState.ReturnUrl, "oauth_code", exchangeCode));
    }

    private async Task<ServiceResult<Uri>> FailCallbackAsync(
        string provider,
        RequestMetadata metadata,
        string reason,
        CancellationToken cancellationToken,
        AuthError authError = AuthError.Unauthorized,
        string errorCode = "oauth_callback_failed")
    {
        await WriteOAuthFailureAsync(provider, metadata, reason, cancellationToken).ConfigureAwait(false);
        return ServiceResult<Uri>.Failure(authError, errorCode);
    }

    // Exchanges the short-lived local result code for a package access and refresh session.
    public async Task<ServiceResult<SessionTokens>> ExchangeAsync(
        string provider,
        string? exchangeCode,
        Guid? tenantId,
        RequestMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        IExternalOAuthProvider? externalProvider = FindEnabledProvider(provider);
        if (externalProvider is null
            || string.IsNullOrWhiteSpace(exchangeCode)
            || exchangeCode.Length > 1_024)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_oauth_exchange");
        }

        OneTimeTokenRecord? token = null;
        foreach (string exchangeHash in _tokens.HashCandidates(exchangeCode))
        {
            token = await _store.ConsumeOneTimeTokenAsync(
                ExchangePurpose(provider),
                exchangeHash,
                _clock.UtcNow,
                cancellationToken).ConfigureAwait(false);
            if (token is not null)
            {
                break;
            }
        }
        if (token is null)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_oauth_exchange");
        }

        AuthUser? user = await _store.FindUserByIdAsync(token.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || !user.IsActive || !user.EmailVerifiedUtc.HasValue)
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "invalid_oauth_exchange");
        }

        if (tenantId.HasValue
            && (!_options.Features.Tenancy
                || !await _store.IsTenantMemberAsync(user.Id, tenantId.Value, cancellationToken).ConfigureAwait(false)))
        {
            return ServiceResult<SessionTokens>.Failure(AuthError.Forbidden, "tenant_access_denied");
        }

        return await _sessionIssuer.IssueSessionAsync(
            user,
            tenantId,
            null,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    // Finds a configured provider without allowing unknown provider names to alter persistence keys.
    private IExternalOAuthProvider? FindEnabledProvider(string provider) =>
        !string.IsNullOrWhiteSpace(provider)
            ? _providers.FirstOrDefault(candidate => candidate.IsEnabled(provider))
            : null;

    // Builds a provider-specific one-time exchange purpose.
    private static string ExchangePurpose(string provider) => ExchangePurposePrefix + provider;

    // Ensures provider-controlled identity claims fit persistence and telemetry boundaries.
    private static bool HasBoundedIdentityClaims(OAuthProviderIdentity identity) =>
        !string.IsNullOrWhiteSpace(identity.Subject)
        && identity.Subject.Length <= MaximumProviderSubjectLength
        && !identity.Subject.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(identity.Email)
        && identity.Email.Length <= MaximumProviderEmailLength
        && string.Equals(identity.Email, identity.Email.Trim(), StringComparison.Ordinal)
        && !identity.Email.Any(char.IsControl)
        && (identity.DisplayName is null
            || (identity.DisplayName.Length <= MaximumProviderDisplayNameLength
                && !identity.DisplayName.Any(char.IsControl)));

    // Appends a one-time code to a local return URL fragment so it is not sent in referrer headers.
    private Uri AppendFragment(string returnUrl, string name, string value)
    {
        Uri absolute = new(_options.BaseUri, returnUrl);
        UriBuilder builder = new(absolute)
        {
            Fragment = $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}"
        };
        return builder.Uri;
    }

    // Encodes a SHA-256 PKCE digest without padding.
    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Writes a sanitized failed OAuth event.
    private Task WriteOAuthFailureAsync(
        string provider,
        RequestMetadata metadata,
        string reason,
        CancellationToken cancellationToken) =>
        OAuthAuditWriter.WriteFailureAsync(_audit, provider, metadata, reason, cancellationToken);

    private sealed record OAuthProtectedPayload(string CodeVerifier, string Nonce);
}

internal sealed class OpenIdConnectOAuthProvider : IExternalOAuthProvider
{
    internal const string HttpClientName = "SharpAccess.OpenIdConnect";
    private const string JsonWebKeyCacheKeyPrefix = "SharpAccess.OpenIdConnect.Jwks:";
    private const int MaximumTokenResponseBytes = 64 * 1024;
    private const int MaximumJsonWebKeySetBytes = 256 * 1024;
    private const int MaximumIdentityTokenCharacters = 48 * 1024;
    private const int MaximumSubjectCharacters = 256;
    private const int MaximumEmailCharacters = 320;
    private const int MaximumDisplayNameCharacters = 200;
    private const int MaximumNonceCharacters = 256;
    private const int MaximumAuthorizedPartyCharacters = 512;
    private const int MaximumAudienceCharacters = 512;
    private const int MaximumIssuedAtCharacters = 12;
    private const int MaximumAudiences = 8;
    private static readonly TimeSpan IdentityTokenClockSkew = TimeSpan.FromMinutes(2);
    private static readonly Type[] ExternalProviderFailureTypes =
    [
        typeof(HttpRequestException),
        typeof(IOException),
        typeof(JsonException),
        typeof(SecurityTokenException),
        typeof(ArgumentException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException)
    ];
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IAuthClock _clock;
    private readonly AuthOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    // Creates the configured OpenID Connect adapter used by the provider-neutral OAuth service.
    public OpenIdConnectOAuthProvider(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IAuthClock clock,
        IOptions<AuthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _clock = clock;
        _options = options.Value;
    }

    // Reports whether the keyed provider is configured and enabled.
    public bool IsEnabled(string provider) =>
        _options.OpenIdConnect.Providers.TryGetValue(provider, out OpenIdConnectProviderOptions? configured)
        && configured.Enabled;

    // Creates a provider authorization endpoint with hardened OAuth 2.1 parameters.
    public Uri CreateAuthorizationUri(string provider, string state, string codeChallenge, string nonce)
    {
        OpenIdConnectProviderOptions configured = GetProvider(provider);
        UriBuilder builder = new(configured.AuthorizationEndpoint);
        Dictionary<string, string> parameters = new()
        {
            ["client_id"] = configured.ClientId,
            ["redirect_uri"] = new Uri(_options.BaseUri, configured.CallbackPath).ToString(),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', configured.Scopes),
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };
        if (!string.IsNullOrWhiteSpace(configured.Prompt))
        {
            parameters["prompt"] = configured.Prompt;
        }

        string query = string.Join(
            '&',
            parameters.Select(static pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        builder.Query = query;
        return builder.Uri;
    }

    // Exchanges the provider authorization code and validates the identity token.
    public async Task<OAuthProviderIdentity?> ExchangeAndValidateAsync(
        string provider,
        string code,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExchangeAndValidateCoreAsync(
                provider,
                code,
                codeVerifier,
                expectedNonce,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExternalProviderFailure(exception, cancellationToken))
        {
            throw new ExternalOAuthProviderException();
        }
    }

    // Performs the bounded token exchange and strict identity-token validation.
    private async Task<OAuthProviderIdentity?> ExchangeAndValidateCoreAsync(
        string provider,
        string code,
        string codeVerifier,
        string expectedNonce,
        CancellationToken cancellationToken)
    {
        OpenIdConnectProviderOptions configured = GetProvider(provider);
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        Dictionary<string, string> tokenRequest = new()
        {
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = new Uri(_options.BaseUri, configured.CallbackPath).ToString()
        };
        using HttpRequestMessage request = new(HttpMethod.Post, configured.TokenEndpoint);
        ApplyClientAuthentication(request, tokenRequest, configured);
        request.Content = new FormUrlEncodedContent(tokenRequest);
        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || !ResponseStayedOnAllowedHost(response, configured))
        {
            return null;
        }

        await using Stream responseStream = await ReadBoundedContentAsync(
            response.Content,
            MaximumTokenResponseBytes,
            cancellationToken).ConfigureAwait(false);
        OpenIdConnectTokenResponse? tokenResponse = await JsonSerializer.DeserializeAsync<OpenIdConnectTokenResponse>(
            responseStream,
            _jsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(tokenResponse?.IdToken)
            || tokenResponse.IdToken.Length > MaximumIdentityTokenCharacters)
        {
            return null;
        }

        JsonWebKeySet keys = await GetKeysAsync(client, provider, configured, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        TokenValidationResult result = await ValidateIdentityTokenAsync(tokenResponse.IdToken, keys, configured).ConfigureAwait(false);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            keys = await GetKeysAsync(client, provider, configured, forceRefresh: true, cancellationToken).ConfigureAwait(false);
            result = await ValidateIdentityTokenAsync(tokenResponse.IdToken, keys, configured).ConfigureAwait(false);
        }

        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return null;
        }

        return ValidateIdentityClaims(result.ClaimsIdentity, configured, expectedNonce, _clock.UtcNow);
    }

    // Applies exactly one configured OAuth token-endpoint client authentication method.
    private static void ApplyClientAuthentication(
        HttpRequestMessage request,
        Dictionary<string, string> tokenRequest,
        OpenIdConnectProviderOptions configured)
    {
        switch (configured.ClientAuthenticationMethod)
        {
            case OpenIdConnectClientAuthenticationMethod.ClientSecretPost:
                tokenRequest["client_id"] = configured.ClientId;
                tokenRequest["client_secret"] = configured.ClientSecret;
                return;
            case OpenIdConnectClientAuthenticationMethod.ClientSecretBasic:
                string encodedClientId = WebUtility.UrlEncode(configured.ClientId);
                string encodedClientSecret = WebUtility.UrlEncode(configured.ClientSecret);
                string credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{encodedClientId}:{encodedClientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                return;
            default:
                throw new InvalidOperationException("The configured token-endpoint client authentication method is invalid.");
        }
    }

    // Validates bounded identity claims and OIDC authorized-party rules before persistence.
    internal static OAuthProviderIdentity? ValidateIdentityClaims(
        ClaimsIdentity claims,
        OpenIdConnectProviderOptions configured,
        string expectedNonce,
        DateTimeOffset now)
    {
        IdentityClaimSet values = ReadIdentityClaims(claims);
        if (!IsValidSubject(values.Subject)
            || !IsValidEmail(values.Email)
            || !IsVerifiedEmail(values.EmailVerified)
            || !IsValidNonce(values.Nonce, expectedNonce)
            || !HasValidIssuedAt(values.IssuedAtClaims, now)
            || !HasValidAudiences(values.Audiences, configured.ClientId)
            || !HasValidAuthorizedParty(values.AuthorizedParty, values.Audiences.Length, configured.ClientId)
            || !IsValidDisplayName(values.DisplayName))
        {
            return null;
        }

        return new OAuthProviderIdentity(values.Subject!, values.Email!, EmailVerified: true, values.DisplayName);
    }

    private static IdentityClaimSet ReadIdentityClaims(ClaimsIdentity claims) => new(
        ReadClaimValue(claims, JwtRegisteredClaimNames.Sub, "sub"),
        ReadClaimValue(claims, JwtRegisteredClaimNames.Email, "email"),
        ReadClaimValue(claims, "email_verified"),
        ReadClaimValue(claims, "nonce"),
        ReadClaimValue(claims, "azp"),
        ReadClaimValue(claims, "name"),
        ReadClaims(claims, JwtRegisteredClaimNames.Iat),
        ReadClaimValues(claims, JwtRegisteredClaimNames.Aud));

    private static string? ReadClaimValue(
        ClaimsIdentity claims,
        string claimType) =>
        claims.FindFirst(claimType)?.Value;

    private static string? ReadClaimValue(
        ClaimsIdentity claims,
        string primaryType,
        string fallbackType)
    {
        Claim? primary = claims.FindFirst(primaryType);
        return primary is not null
            ? primary.Value
            : ReadClaimValue(claims, fallbackType);
    }

    private static Claim[] ReadClaims(
        ClaimsIdentity claims,
        string claimType) =>
        claims.FindAll(claimType).ToArray();

    private static string[] ReadClaimValues(
        ClaimsIdentity claims,
        string claimType) =>
        claims.FindAll(claimType)
            .Select(static claim => claim.Value)
            .ToArray();

    private static bool IsValidSubject(string? subject) =>
        !string.IsNullOrWhiteSpace(subject)
        && subject.Length <= MaximumSubjectCharacters
        && !subject.Any(char.IsControl);

    private static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= MaximumEmailCharacters
        && string.Equals(email, email.Trim(), StringComparison.Ordinal)
        && !email.Any(char.IsControl);

    private static bool IsVerifiedEmail(string? emailVerified) =>
        emailVerified is not null
        && emailVerified.Length <= 5
        && string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidNonce(string? nonce, string expectedNonce) =>
        nonce is not null
        && nonce.Length <= MaximumNonceCharacters
        && string.Equals(nonce, expectedNonce, StringComparison.Ordinal);

    private static bool HasValidAudiences(string[] audiences, string clientId) =>
        audiences.Length is > 0 and <= MaximumAudiences
        && audiences.All(IsValidAudience)
        && audiences.Contains(clientId, StringComparer.Ordinal);

    private static bool IsValidAudience(string audience) =>
        !string.IsNullOrWhiteSpace(audience)
        && audience.Length <= MaximumAudienceCharacters
        && string.Equals(audience, audience.Trim(), StringComparison.Ordinal)
        && !audience.Any(char.IsControl);

    private static bool HasValidAuthorizedParty(
        string? authorizedParty,
        int audienceCount,
        string clientId)
    {
        if (authorizedParty?.Length > MaximumAuthorizedPartyCharacters)
        {
            return false;
        }

        if (audienceCount > 1)
        {
            return string.Equals(authorizedParty, clientId, StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(authorizedParty)
            || string.Equals(authorizedParty, clientId, StringComparison.Ordinal);
    }

    private static bool IsValidDisplayName(string? displayName) =>
        displayName is null
        || (displayName.Length <= MaximumDisplayNameCharacters
            && !displayName.Any(char.IsControl));

    // Requires one bounded Unix-seconds issued-at value no later than the project clock plus skew.
    private static bool HasValidIssuedAt(Claim[] issuedAtClaims, DateTimeOffset now)
    {
        if (issuedAtClaims.Length != 1)
        {
            return false;
        }

        string value = issuedAtClaims[0].Value;
        if (value.Length is 0 or > MaximumIssuedAtCharacters
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long issuedAtSeconds)
            || issuedAtSeconds < 0
            || issuedAtSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        long latestAccepted = now.ToUnixTimeSeconds() + (long)IdentityTokenClockSkew.TotalSeconds;
        return issuedAtSeconds <= latestAccepted;
    }

    // Validates issuer, audience, lifetime, signature, and the configured algorithm allowlist.
    private static Task<TokenValidationResult> ValidateIdentityTokenAsync(
        string identityToken,
        JsonWebKeySet keys,
        OpenIdConnectProviderOptions configured)
    {
        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuers = configured.ValidIssuers,
            ValidateAudience = true,
            ValidAudience = configured.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys.Keys,
            ValidAlgorithms = configured.SigningAlgorithms,
            ClockSkew = IdentityTokenClockSkew
        };
        JsonWebTokenHandler handler = new();
        return handler.ValidateTokenAsync(identityToken, parameters);
    }

    // Classifies bounded transport and untrusted payload errors without swallowing caller cancellation.
    private static bool IsExternalProviderFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        Type exceptionType = exception.GetType();
        return Array.Exists(ExternalProviderFailureTypes, type => type.IsAssignableFrom(exceptionType));
    }

    // Retrieves and caches one provider's signing keys.
    private async Task<JsonWebKeySet> GetKeysAsync(
        HttpClient client,
        string provider,
        OpenIdConnectProviderOptions configured,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string cacheKey = JsonWebKeyCacheKeyPrefix + provider;
        if (!forceRefresh
            && _cache.TryGetValue(cacheKey, out JsonWebKeySet? cached)
            && cached is not null)
        {
            return cached;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, configured.JsonWebKeySetEndpoint);
        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!ResponseStayedOnAllowedHost(response, configured))
        {
            throw new HttpRequestException("The OpenID Connect response left the configured host allowlist.");
        }
        await using Stream jwksStream = await ReadBoundedContentAsync(
            response.Content,
            MaximumJsonWebKeySetBytes,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(jwksStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonWebKeySet keys = new(document.RootElement.GetRawText());
        _cache.Set(cacheKey, keys, TimeSpan.FromHours(6));
        return keys;
    }

    // Resolves only a configured and enabled provider key.
    private OpenIdConnectProviderOptions GetProvider(string provider) =>
        _options.OpenIdConnect.Providers.TryGetValue(provider, out OpenIdConnectProviderOptions? configured)
        && configured.Enabled
            ? configured
            : throw new InvalidOperationException("The OpenID Connect provider is not enabled.");

    // Rejects redirected responses whose final HTTPS host is outside the provider allowlist.
    private static bool ResponseStayedOnAllowedHost(
        HttpResponseMessage response,
        OpenIdConnectProviderOptions configured)
    {
        Uri? finalUri = response.RequestMessage?.RequestUri;
        return finalUri is not null
            && finalUri.Scheme == Uri.UriSchemeHttps
            && configured.AllowedHosts.Contains(finalUri.IdnHost, StringComparer.OrdinalIgnoreCase);
    }

    // Copies a response into a bounded in-memory stream before parsing untrusted JSON.
    private static async Task<Stream> ReadBoundedContentAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException("The OAuth provider response exceeded its configured safety limit.");
        }

        Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        MemoryStream destination = new(Math.Min(maximumBytes, 16 * 1024));
        byte[] buffer = new byte[8 * 1024];
        try
        {
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    destination.Position = 0;
                    return destination;
                }

                total = checked(total + read);
                if (total > maximumBytes)
                {
                    throw new InvalidDataException("The OAuth provider response exceeded its configured safety limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await destination.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record IdentityClaimSet(
        string? Subject,
        string? Email,
        string? EmailVerified,
        string? Nonce,
        string? AuthorizedParty,
        string? DisplayName,
        Claim[] IssuedAtClaims,
        string[] Audiences);

    private sealed record OpenIdConnectTokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
