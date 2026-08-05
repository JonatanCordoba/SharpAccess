using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.OAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SharpAccess.IntegrationTests;

[Trait("Category", "OidcLive")]
public sealed class OidcLiveSmokeTests
{
    // Exchanges one just-in-time authorization code and validates the resulting identity against the real provider's live JWKS.
    [OidcLiveFact]
    public async Task RealProviderAuthorizationCodeCanBeExchangedAndValidated()
    {
        LiveOidcSettings settings = LiveOidcSettings.Load();
        AuthOptions options = settings.CreateOptions();
        using MemoryCache cache = new(new MemoryCacheOptions());
        using LiveHttpClientFactory clients = new();
        OpenIdConnectOAuthProvider provider = new(
            clients,
            cache,
            new SystemAuthClock(),
            Options.Create(options));

        OAuthProviderIdentity? identity;
        try
        {
            identity = await provider.ExchangeAndValidateAsync(
                settings.Provider,
                settings.AuthorizationCode,
                settings.CodeVerifier,
                settings.Nonce);
        }
        catch (ExternalOAuthProviderException exception)
        {
            throw new InvalidOperationException(
                clients.DescribeFailure(),
                exception);
        }

        Assert.True(identity is not null, clients.DescribeFailure());
        Assert.True(identity!.EmailVerified);
        Assert.False(string.IsNullOrWhiteSpace(identity.Subject));
        Assert.False(string.IsNullOrWhiteSpace(identity.Email));
    }

    private sealed record LiveOidcSettings(
        string Provider,
        string ClientId,
        string ClientSecret,
        OpenIdConnectClientAuthenticationMethod ClientAuthenticationMethod,
        Uri BaseUri,
        string CallbackPath,
        Uri AuthorizationEndpoint,
        Uri TokenEndpoint,
        Uri JsonWebKeySetEndpoint,
        IList<string> ValidIssuers,
        IList<string> SigningAlgorithms,
        IList<string> AllowedHosts,
        string AuthorizationCode,
        string CodeVerifier,
        string Nonce)
    {
        // Reads the protected environment contract without returning secret values in diagnostics.
        internal static LiveOidcSettings Load() => new(
            Required("SHARPACCESS_OIDC_LIVE_PROVIDER"),
            Required("SHARPACCESS_OIDC_LIVE_CLIENT_ID"),
            Required("SHARPACCESS_OIDC_LIVE_CLIENT_SECRET"),
            ParseAuthenticationMethod(Required("SHARPACCESS_OIDC_LIVE_CLIENT_AUTHENTICATION_METHOD")),
            RequiredUri("SHARPACCESS_OIDC_LIVE_BASE_URI"),
            Required("SHARPACCESS_OIDC_LIVE_CALLBACK_PATH"),
            RequiredUri("SHARPACCESS_OIDC_LIVE_AUTHORIZATION_ENDPOINT"),
            RequiredUri("SHARPACCESS_OIDC_LIVE_TOKEN_ENDPOINT"),
            RequiredUri("SHARPACCESS_OIDC_LIVE_JWKS_ENDPOINT"),
            RequiredList("SHARPACCESS_OIDC_LIVE_VALID_ISSUERS"),
            RequiredList("SHARPACCESS_OIDC_LIVE_SIGNING_ALGORITHMS"),
            RequiredList("SHARPACCESS_OIDC_LIVE_ALLOWED_HOSTS"),
            Required("SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE"),
            Required("SHARPACCESS_OIDC_LIVE_CODE_VERIFIER"),
            Required("SHARPACCESS_OIDC_LIVE_NONCE"));

        // Builds the same keyed provider options used by production without enabling any Google-specific path.
        internal AuthOptions CreateOptions()
        {
            AuthOptions options = new() { BaseUri = BaseUri };
            options.OpenIdConnect.Providers.Clear();
            options.OpenIdConnect.Providers[Provider] = new OpenIdConnectProviderOptions
            {
                Enabled = true,
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                ClientAuthenticationMethod = ClientAuthenticationMethod,
                CallbackPath = CallbackPath,
                AuthorizationEndpoint = AuthorizationEndpoint,
                TokenEndpoint = TokenEndpoint,
                JsonWebKeySetEndpoint = JsonWebKeySetEndpoint,
                ValidIssuers = ValidIssuers,
                Scopes = ["openid", "email"],
                SigningAlgorithms = SigningAlgorithms,
                AllowedHosts = AllowedHosts
            };
            return options;
        }

        private static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Required live OIDC setting is missing: {name}.");

        private static Uri RequiredUri(string name) =>
            Uri.TryCreate(Required(name), UriKind.Absolute, out Uri? value)
                ? value
                : throw new InvalidOperationException($"Required live OIDC URI setting is invalid: {name}.");

        private static string[] RequiredList(string name)
        {
            string[] values = Required(name)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return values.Length > 0
                ? values
                : throw new InvalidOperationException($"Required live OIDC list setting is empty: {name}.");
        }

        private static OpenIdConnectClientAuthenticationMethod ParseAuthenticationMethod(string value) =>
            Enum.TryParse(value, ignoreCase: false, out OpenIdConnectClientAuthenticationMethod parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new InvalidOperationException(
                        "SHARPACCESS_OIDC_LIVE_CLIENT_AUTHENTICATION_METHOD must be ClientSecretPost or ClientSecretBasic.");
    }

    private sealed class LiveHttpClientFactory : IHttpClientFactory, IDisposable
    {
        private readonly LiveRequestDiagnosticsHandler _diagnostics;
        private readonly HttpClient _client;

        // Creates the bounded real-provider client used only by the opt-in smoke.
        public LiveHttpClientFactory()
        {
            _diagnostics = new LiveRequestDiagnosticsHandler(
                new HttpClientHandler { AllowAutoRedirect = false });
            _client = new HttpClient(_diagnostics)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpAccess-OidcLiveSmoke/1.0");
        }

        // Returns the single bounded client and rejects unexpected client names.
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, name);
            return _client;
        }

        // Returns only bounded request-stage metadata and never reads or records provider payloads.
        internal string DescribeFailure() => _diagnostics.DescribeFailure();

        public void Dispose() => _client.Dispose();
    }

    private sealed class LiveRequestDiagnosticsHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
    {
        private int _tokenRequests;
        private int _jwksRequests;
        private int? _tokenStatus;
        private int? _jwksStatus;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            if (request.Method == HttpMethod.Post)
            {
                _tokenRequests++;
                _tokenStatus = (int)response.StatusCode;
            }
            else if (request.Method == HttpMethod.Get)
            {
                _jwksRequests++;
                _jwksStatus = (int)response.StatusCode;
            }

            return response;
        }

        internal string DescribeFailure() =>
            "Live OIDC identity was rejected without exposing provider payloads. "
            + $"token_requests={_tokenRequests}; token_status={FormatStatus(_tokenStatus)}; "
            + $"jwks_requests={_jwksRequests}; jwks_status={FormatStatus(_jwksStatus)}.";

        private static string FormatStatus(int? status) =>
            status.HasValue
                ? status.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "not-observed";
    }
}

internal sealed class OidcLiveFactAttribute : FactAttribute
{
    private static readonly string[] RequiredEnvironmentVariables =
    [
        "SHARPACCESS_OIDC_LIVE_PROVIDER",
        "SHARPACCESS_OIDC_LIVE_CLIENT_ID",
        "SHARPACCESS_OIDC_LIVE_CLIENT_SECRET",
        "SHARPACCESS_OIDC_LIVE_CLIENT_AUTHENTICATION_METHOD",
        "SHARPACCESS_OIDC_LIVE_BASE_URI",
        "SHARPACCESS_OIDC_LIVE_CALLBACK_PATH",
        "SHARPACCESS_OIDC_LIVE_AUTHORIZATION_ENDPOINT",
        "SHARPACCESS_OIDC_LIVE_TOKEN_ENDPOINT",
        "SHARPACCESS_OIDC_LIVE_JWKS_ENDPOINT",
        "SHARPACCESS_OIDC_LIVE_VALID_ISSUERS",
        "SHARPACCESS_OIDC_LIVE_SIGNING_ALGORITHMS",
        "SHARPACCESS_OIDC_LIVE_ALLOWED_HOSTS",
        "SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE",
        "SHARPACCESS_OIDC_LIVE_CODE_VERIFIER",
        "SHARPACCESS_OIDC_LIVE_NONCE"
    ];

    // Skips normal verification while allowing the protected workflow to opt in with a complete just-in-time secret set.
    public OidcLiveFactAttribute()
    {
        string[] missing = RequiredEnvironmentVariables
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();
        if (missing.Length > 0)
        {
            Skip = "Set the protected SHARPACCESS_OIDC_LIVE_* environment contract to run the real-provider smoke.";
        }
    }
}
