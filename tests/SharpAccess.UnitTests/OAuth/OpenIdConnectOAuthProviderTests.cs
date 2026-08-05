using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using SharpAccess.Configuration;
using SharpAccess.OAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;

namespace SharpAccess.UnitTests;

public sealed class OpenIdConnectOAuthProviderTests
{
    // Verifies the generic contract retains one safe disabled Google-compatible example.
    [Fact]
    public void DefaultConfigurationRetainsOneDisabledGoogleCompatibleEntry()
    {
        AuthOptions options = new();

        OpenIdConnectProviderOptions google = Assert.Single(options.OpenIdConnect.Providers).Value;
        Assert.False(google.Enabled);
        Assert.Equal(
            OpenIdConnectClientAuthenticationMethod.ClientSecretPost,
            google.ClientAuthenticationMethod);
        Assert.Equal("/auth/oauth/google/callback", google.CallbackPath);
        Assert.Contains("https://accounts.google.com", google.ValidIssuers);
        Assert.Equal(["openid", "email", "profile"], google.Scopes);
        Assert.Equal(["RS256"], google.SigningAlgorithms);
        Assert.Contains("accounts.google.com", google.AllowedHosts);
    }

    // Verifies Core registers the bounded named OpenID Connect HTTP client.
    [Fact]
    public void AddSharpAccessConfiguresTheOpenIdConnectClientUsedByTheProvider()
    {
        ServiceCollection services = new();

        services.AddSharpAccess(static _ => { });
        using ServiceProvider provider = services.BuildServiceProvider();
        using HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(OpenIdConnectOAuthProvider.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
        Assert.Contains(
            client.DefaultRequestHeaders.UserAgent,
            value => string.Equals(value.Product?.Name, "SharpAccess", StringComparison.Ordinal)
                && string.Equals(value.Product?.Version, "1.0", StringComparison.Ordinal));
    }

    // Verifies provider requests resolve the configured named client.
    [Fact]
    public async Task OpenIdConnectProviderUsesConfiguredNamedClient()
    {
        CapturingHttpClientFactory factory = new();
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            factory,
            cache,
            TestOptions.Clock,
            Options.Create(OAuthOptions()));

        OAuthProviderIdentity? identity = await provider.ExchangeAndValidateAsync(
            "google",
            "code",
            "verifier",
            "nonce");

        Assert.Null(identity);
        Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, factory.ClientName);
        Assert.Equal("SharpAccess.OpenIdConnect", factory.ClientName);
    }

    // Verifies authorization requests preserve code flow, PKCE S256, state, and nonce.
    [Fact]
    public void ConfiguredProviderBuildsAuthorizationCodePkceAndNonceRequest()
    {
        AuthOptions options = OAuthOptions();
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new CapturingHttpClientFactory(),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        Uri authorization = provider.CreateAuthorizationUri(
            "google",
            "state-value",
            "challenge-value",
            "nonce-value");
        var query = QueryHelpers.ParseQuery(authorization.Query);

        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("https://app.test/auth/oauth/google/callback", query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal("state-value", query["state"]);
        Assert.Equal("nonce-value", query["nonce"]);
        Assert.Equal("challenge-value", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
        Assert.False(provider.IsEnabled("unknown"));
    }

    // Verifies token responses cannot escape the configured endpoint trust boundary.
    [Fact]
    public async Task TokenExchangeRejectsResponsesOutsideTheProviderHostAllowlist()
    {
        AuthOptions options = OAuthOptions();
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleHandlerFactory(new UnsafeFinalHostHandler()),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        OAuthProviderIdentity? identity = await provider.ExchangeAndValidateAsync(
            "google",
            "code",
            "verifier",
            "nonce");

        Assert.Null(identity);
    }

    // Verifies both token-endpoint authentication methods use one nonduplicated credential shape.
    [Theory]
    [InlineData(OpenIdConnectClientAuthenticationMethod.ClientSecretPost)]
    [InlineData(OpenIdConnectClientAuthenticationMethod.ClientSecretBasic)]
    public async Task TokenEndpointClientAuthenticationUsesConfiguredRequestShape(
        OpenIdConnectClientAuthenticationMethod authenticationMethod)
    {
        AuthOptions options = OAuthOptions();
        TestOptions.Google(options).ClientAuthenticationMethod = authenticationMethod;
        TokenRequestCaptureHandler handler = new();
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleHandlerFactory(handler),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        Assert.Null(await provider.ExchangeAndValidateAsync(
            "google",
            "authorization-code",
            "pkce-verifier",
            "nonce"));

        Assert.NotNull(handler.Body);
        var form = QueryHelpers.ParseQuery($"?{handler.Body}");
        Assert.Equal("authorization-code", form["code"]);
        Assert.Equal("pkce-verifier", form["code_verifier"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("https://app.test/auth/oauth/google/callback", form["redirect_uri"]);
        if (authenticationMethod == OpenIdConnectClientAuthenticationMethod.ClientSecretPost)
        {
            Assert.Null(handler.AuthorizationScheme);
            Assert.Equal("client-id", form["client_id"]);
            Assert.Equal("client-secret-value", form["client_secret"]);
        }
        else
        {
            Assert.Equal("Basic", handler.AuthorizationScheme);
            Assert.Equal(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret-value")),
                handler.AuthorizationParameter);
            Assert.False(form.ContainsKey("client_id"));
            Assert.False(form.ContainsKey("client_secret"));
        }
    }

    // Verifies malformed provider payloads produce one sanitized exception contract.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedTokenOrJsonWebKeySetPayloadIsSanitized(bool malformedTokenResponse)
    {
        AuthOptions options = OAuthOptions();
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleHandlerFactory(new MalformedPayloadHandler(malformedTokenResponse)),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        ExternalOAuthProviderException exception = await Assert.ThrowsAsync<ExternalOAuthProviderException>(
            () => provider.ExchangeAndValidateAsync(
                "google",
                "code",
                "verifier",
                "nonce"));

        Assert.Equal(
            "The external identity provider response could not be processed.",
            exception.Message);
        Assert.Null(exception.InnerException);
    }

    // Verifies that OIDC multi-audience identity tokens require an authorized party bound to the client.
    [Fact]
    public void MultiAudienceIdentityRequiresMatchingAuthorizedParty()
    {
        OpenIdConnectProviderOptions configured = TestOptions.Google(OAuthOptions());
        ClaimsIdentity missingAuthorizedParty = IdentityClaims(["client-id", "other-audience"]);

        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            missingAuthorizedParty,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity matchingAuthorizedParty = IdentityClaims(["client-id", "other-audience"]);
        matchingAuthorizedParty.AddClaim(new Claim("azp", "client-id"));
        Assert.NotNull(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            matchingAuthorizedParty,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity mismatchedAuthorizedParty = IdentityClaims(["client-id", "other-audience"]);
        mismatchedAuthorizedParty.AddClaim(new Claim("azp", "different-client"));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            mismatchedAuthorizedParty,
            configured,
            "nonce",
            TestOptions.Now));
    }

    // Verifies that single-audience tokens keep optional azp behavior while rejecting a mismatch.
    [Fact]
    public void SingleAudienceIdentityAllowsMissingAuthorizedPartyButRejectsMismatch()
    {
        OpenIdConnectProviderOptions configured = TestOptions.Google(OAuthOptions());
        Assert.NotNull(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            IdentityClaims(["client-id"]),
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity mismatchedAuthorizedParty = IdentityClaims(["client-id"]);
        mismatchedAuthorizedParty.AddClaim(new Claim("azp", "different-client"));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            mismatchedAuthorizedParty,
            configured,
            "nonce",
            TestOptions.Now));
    }

    // Verifies that untrusted audience collections and values remain explicitly bounded.
    [Fact]
    public void IdentityAudienceClaimsAreBounded()
    {
        OpenIdConnectProviderOptions configured = TestOptions.Google(OAuthOptions());
        ClaimsIdentity tooMany = IdentityClaims(
            Enumerable.Range(0, 9).Select(index => $"audience-{index}"));
        tooMany.AddClaim(new Claim("azp", "client-id"));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            tooMany,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity oversized = IdentityClaims([new string('a', 513)]);
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            oversized,
            configured,
            "nonce",
            TestOptions.Now));
    }

    // Verifies OIDC issued-at is exactly one bounded numeric value within project-clock skew.
    [Fact]
    public void IdentityIssuedAtClaimIsRequiredBoundedAndClockChecked()
    {
        OpenIdConnectProviderOptions configured = TestOptions.Google(OAuthOptions());
        ClaimsIdentity missing = IdentityClaims(["client-id"]);
        missing.RemoveClaim(missing.FindFirst("iat")!);
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            missing,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity duplicate = IdentityClaims(["client-id"]);
        duplicate.AddClaim(IssuedAtClaim(TestOptions.Now));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            duplicate,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity malformed = IdentityClaims(["client-id"]);
        malformed.RemoveClaim(malformed.FindFirst("iat")!);
        malformed.AddClaim(new Claim("iat", "not-a-number"));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            malformed,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity oversized = IdentityClaims(["client-id"]);
        oversized.RemoveClaim(oversized.FindFirst("iat")!);
        oversized.AddClaim(new Claim("iat", new string('1', 13)));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            oversized,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity outsideSkew = IdentityClaims(["client-id"]);
        outsideSkew.RemoveClaim(outsideSkew.FindFirst("iat")!);
        outsideSkew.AddClaim(IssuedAtClaim(TestOptions.Now.AddMinutes(2).AddSeconds(1)));
        Assert.Null(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            outsideSkew,
            configured,
            "nonce",
            TestOptions.Now));

        ClaimsIdentity atSkewBoundary = IdentityClaims(["client-id"]);
        atSkewBoundary.RemoveClaim(atSkewBoundary.FindFirst("iat")!);
        atSkewBoundary.AddClaim(IssuedAtClaim(TestOptions.Now.AddMinutes(2)));
        Assert.NotNull(OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            atSkewBoundary,
            configured,
            "nonce",
            TestOptions.Now));
    }

    // Creates one enabled provider configuration for adapter tests.
    private static AuthOptions OAuthOptions()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        google.AuthorizationEndpoint = new Uri("https://oauth.example/auth");
        google.TokenEndpoint = new Uri("https://oauth.example/token");
        google.JsonWebKeySetEndpoint = new Uri("https://oauth.example/jwks");
        google.ValidIssuers = ["https://oauth.example"];
        google.AllowedHosts = ["oauth.example"];
        return options;
    }

    // Creates a valid baseline identity with the supplied untrusted audience claims.
    private static ClaimsIdentity IdentityClaims(IEnumerable<string> audiences)
    {
        ClaimsIdentity identity = new([
            new Claim("sub", "provider-subject"),
            new Claim("email", "person@example.com"),
            new Claim("email_verified", "true"),
            new Claim("nonce", "nonce"),
            IssuedAtClaim(TestOptions.Now)
        ]);
        identity.AddClaims(audiences.Select(static audience => new Claim("aud", audience)));
        return identity;
    }

    // Creates one numeric Unix-seconds issued-at claim.
    private static Claim IssuedAtClaim(DateTimeOffset value) => new(
        "iat",
        value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        ClaimValueTypes.Integer64);

    private sealed class CapturingHttpClientFactory : IHttpClientFactory
    {
        public string? ClientName { get; private set; }

        // Captures the requested name and returns a deterministic failing client.
        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return new HttpClient(new FailureHandler());
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        // Returns a deterministic upstream failure without network access.
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        // Returns a client over the supplied deterministic handler.
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class UnsafeFinalHostHandler : HttpMessageHandler
    {
        // Returns a response whose final request host is outside the allowlist.
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id_token\":\"untrusted\"}"),
                RequestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://outside.example/token")
            });
    }

    private sealed class MalformedPayloadHandler(bool malformedTokenResponse) : HttpMessageHandler
    {
        // Returns malformed token or JWKS JSON according to the selected branch.
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string payload = request.Method == HttpMethod.Post
                ? malformedTokenResponse
                    ? "{"
                    : "{\"id_token\":\"not-a-valid-token\"}"
                : "{";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload),
                RequestMessage = request
            });
        }
    }

    private sealed class TokenRequestCaptureHandler : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? Body { get; private set; }

        // Captures the token request before returning a deterministic provider rejection.
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = request
            };
        }
    }
}
