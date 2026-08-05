using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using SharpAccess.Configuration;
using SharpAccess.OAuth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class OpenIdConnectOAuthSecurityTests
{
    [Theory]
    [Trait("MutationInvariant", "OidcIdentityClaims")]
    [InlineData(IdentityClaimFailure.MissingSubject)]
    [InlineData(IdentityClaimFailure.WhitespaceSubject)]
    [InlineData(IdentityClaimFailure.OversizedSubject)]
    [InlineData(IdentityClaimFailure.ControlSubject)]
    [InlineData(IdentityClaimFailure.MissingEmail)]
    [InlineData(IdentityClaimFailure.WhitespaceEmail)]
    [InlineData(IdentityClaimFailure.OversizedEmail)]
    [InlineData(IdentityClaimFailure.PaddedEmail)]
    [InlineData(IdentityClaimFailure.ControlEmail)]
    [InlineData(IdentityClaimFailure.MissingEmailVerified)]
    [InlineData(IdentityClaimFailure.FalseEmailVerified)]
    [InlineData(IdentityClaimFailure.OversizedEmailVerified)]
    [InlineData(IdentityClaimFailure.MissingNonce)]
    [InlineData(IdentityClaimFailure.MismatchedNonce)]
    [InlineData(IdentityClaimFailure.OversizedNonce)]
    [InlineData(IdentityClaimFailure.MissingAudience)]
    [InlineData(IdentityClaimFailure.WhitespaceAudience)]
    [InlineData(IdentityClaimFailure.PaddedAudience)]
    [InlineData(IdentityClaimFailure.ControlAudience)]
    [InlineData(IdentityClaimFailure.MissingClientAudience)]
    [InlineData(IdentityClaimFailure.OversizedAuthorizedParty)]
    [InlineData(IdentityClaimFailure.OversizedDisplayName)]
    [InlineData(IdentityClaimFailure.ControlDisplayName)]
    [InlineData(IdentityClaimFailure.NegativeIssuedAt)]
    [InlineData(IdentityClaimFailure.IssuedAtBeyondDateTimeRange)]
    public void InvalidIdentityClaimBoundariesAreRejected(IdentityClaimFailure failure)
    {
        OpenIdConnectProviderOptions configured = ConfiguredProvider();
        ClaimsIdentity claims = ValidIdentityClaims();
        ApplyFailure(claims, failure);

        OAuthProviderIdentity? identity = OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            claims,
            configured,
            "expected-nonce",
            TestOptions.Now);

        Assert.Null(identity);
    }

    [Fact]
    public void ValidIdentityClaimBoundariesAreAccepted()
    {
        OpenIdConnectProviderOptions configured = ConfiguredProvider();
        ClaimsIdentity claims = ValidIdentityClaims();
        SetClaim(claims, "sub", new string('s', 256));
        SetClaim(claims, "email_verified", "TRUE");
        SetClaim(claims, "name", new string('n', 200));
        SetClaim(claims, "azp", "   ");

        OAuthProviderIdentity? identity = OpenIdConnectOAuthProvider.ValidateIdentityClaims(
            claims,
            configured,
            "expected-nonce",
            TestOptions.Now);

        Assert.NotNull(identity);
        Assert.Equal(new string('s', 256), identity.Subject);
        Assert.Equal(new string('n', 200), identity.DisplayName);
    }

    [Theory]
    [Trait("MutationInvariant", "OidcProviderFailures")]
    [InlineData(ExternalFailure.HttpRequest)]
    [InlineData(ExternalFailure.Io)]
    [InlineData(ExternalFailure.Json)]
    [InlineData(ExternalFailure.SecurityToken)]
    [InlineData(ExternalFailure.Argument)]
    [InlineData(ExternalFailure.InvalidOperation)]
    [InlineData(ExternalFailure.NotSupported)]
    [InlineData(ExternalFailure.UpstreamCancellation)]
    public async Task ExternalProviderFailuresAreSanitized(ExternalFailure failure)
    {
        using ProviderFixture fixture = new(new ThrowingHandler(failure));

        ExternalOAuthProviderException exception = await Assert.ThrowsAsync<ExternalOAuthProviderException>(
            () => fixture.Provider.ExchangeAndValidateAsync(
                "google",
                "code",
                "verifier",
                "expected-nonce"));

        Assert.Equal(
            "The external identity provider response could not be processed.",
            exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task CallerCancellationIsNotSanitized()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        using ProviderFixture fixture = new(new ThrowingHandler(ExternalFailure.UpstreamCancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Provider.ExchangeAndValidateAsync(
                "google",
                "code",
                "verifier",
                "expected-nonce",
                cancellation.Token));
    }

    [Fact]
    public async Task UnknownProgrammingFailureIsNotSanitized()
    {
        using ProviderFixture fixture = new(new ThrowingHandler(ExternalFailure.Unknown));

        await Assert.ThrowsAsync<DivideByZeroException>(
            () => fixture.Provider.ExchangeAndValidateAsync(
                "google",
                "code",
                "verifier",
                "expected-nonce"));
    }

    private static AuthOptions OAuthOptions()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions configured = TestOptions.EnableGoogle(options);
        configured.ClientId = "client-id";
        configured.ClientSecret = "client-secret-value";
        configured.AuthorizationEndpoint = new Uri("https://oauth.example/auth");
        configured.TokenEndpoint = new Uri("https://oauth.example/token");
        configured.JsonWebKeySetEndpoint = new Uri("https://oauth.example/jwks");
        configured.ValidIssuers = ["https://oauth.example"];
        configured.AllowedHosts = ["oauth.example"];
        return options;
    }

    private static OpenIdConnectProviderOptions ConfiguredProvider() =>
        TestOptions.Google(OAuthOptions());

    private static ClaimsIdentity ValidIdentityClaims() => new([
        new Claim("sub", "provider-subject"),
        new Claim("email", "person@example.com"),
        new Claim("email_verified", "true"),
        new Claim("nonce", "expected-nonce"),
        new Claim("aud", "client-id"),
        IssuedAtClaim(TestOptions.Now)
    ]);

    private static Claim IssuedAtClaim(DateTimeOffset value) => new(
        "iat",
        value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        ClaimValueTypes.Integer64);

    private static void ApplyFailure(ClaimsIdentity claims, IdentityClaimFailure failure)
    {
        switch (failure)
        {
            case IdentityClaimFailure.MissingSubject:
                SetClaim(claims, "sub", null);
                break;
            case IdentityClaimFailure.WhitespaceSubject:
                SetClaim(claims, "sub", "   ");
                break;
            case IdentityClaimFailure.OversizedSubject:
                SetClaim(claims, "sub", new string('s', 257));
                break;
            case IdentityClaimFailure.ControlSubject:
                SetClaim(claims, "sub", "subject\u0001");
                break;
            case IdentityClaimFailure.MissingEmail:
                SetClaim(claims, "email", null);
                break;
            case IdentityClaimFailure.WhitespaceEmail:
                SetClaim(claims, "email", "   ");
                break;
            case IdentityClaimFailure.OversizedEmail:
                SetClaim(claims, "email", new string('e', 321));
                break;
            case IdentityClaimFailure.PaddedEmail:
                SetClaim(claims, "email", " person@example.com ");
                break;
            case IdentityClaimFailure.ControlEmail:
                SetClaim(claims, "email", "person@example.com\u0001");
                break;
            case IdentityClaimFailure.MissingEmailVerified:
                SetClaim(claims, "email_verified", null);
                break;
            case IdentityClaimFailure.FalseEmailVerified:
                SetClaim(claims, "email_verified", "false");
                break;
            case IdentityClaimFailure.OversizedEmailVerified:
                SetClaim(claims, "email_verified", "true00");
                break;
            case IdentityClaimFailure.MissingNonce:
                SetClaim(claims, "nonce", null);
                break;
            case IdentityClaimFailure.MismatchedNonce:
                SetClaim(claims, "nonce", "different-nonce");
                break;
            case IdentityClaimFailure.OversizedNonce:
                SetClaim(claims, "nonce", new string('n', 257));
                break;
            case IdentityClaimFailure.MissingAudience:
                SetClaims(claims, "aud", []);
                break;
            case IdentityClaimFailure.WhitespaceAudience:
                SetClaims(claims, "aud", ["   "]);
                break;
            case IdentityClaimFailure.PaddedAudience:
                SetClaims(claims, "aud", [" client-id "]);
                break;
            case IdentityClaimFailure.ControlAudience:
                SetClaims(claims, "aud", ["client-id\u0001"]);
                break;
            case IdentityClaimFailure.MissingClientAudience:
                SetClaims(claims, "aud", ["other-audience"]);
                break;
            case IdentityClaimFailure.OversizedAuthorizedParty:
                SetClaim(claims, "azp", new string('a', 513));
                break;
            case IdentityClaimFailure.OversizedDisplayName:
                SetClaim(claims, "name", new string('n', 201));
                break;
            case IdentityClaimFailure.ControlDisplayName:
                SetClaim(claims, "name", "name\u0001");
                break;
            case IdentityClaimFailure.NegativeIssuedAt:
                SetClaim(claims, "iat", "-1");
                break;
            case IdentityClaimFailure.IssuedAtBeyondDateTimeRange:
                SetClaim(claims, "iat", "253402300800");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failure), failure, null);
        }
    }

    private static void SetClaim(ClaimsIdentity claims, string type, string? value) =>
        SetClaims(claims, type, value is null ? [] : [value]);

    private static void SetClaims(ClaimsIdentity claims, string type, IEnumerable<string> values)
    {
        foreach (Claim claim in claims.FindAll(type).ToArray())
        {
            claims.RemoveClaim(claim);
        }

        claims.AddClaims(values.Select(value => new Claim(type, value)));
    }

    public enum IdentityClaimFailure
    {
        MissingSubject,
        WhitespaceSubject,
        OversizedSubject,
        ControlSubject,
        MissingEmail,
        WhitespaceEmail,
        OversizedEmail,
        PaddedEmail,
        ControlEmail,
        MissingEmailVerified,
        FalseEmailVerified,
        OversizedEmailVerified,
        MissingNonce,
        MismatchedNonce,
        OversizedNonce,
        MissingAudience,
        WhitespaceAudience,
        PaddedAudience,
        ControlAudience,
        MissingClientAudience,
        OversizedAuthorizedParty,
        OversizedDisplayName,
        ControlDisplayName,
        NegativeIssuedAt,
        IssuedAtBeyondDateTimeRange
    }

    public enum ExternalFailure
    {
        HttpRequest,
        Io,
        Json,
        SecurityToken,
        Argument,
        InvalidOperation,
        NotSupported,
        UpstreamCancellation,
        Unknown
    }

    private sealed class ProviderFixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public ProviderFixture(HttpMessageHandler handler)
        {
            Provider = new OpenIdConnectOAuthProvider(
                new SingleHandlerFactory(handler),
                _cache,
                TestOptions.Clock,
                Options.Create(OAuthOptions()));
        }

        public OpenIdConnectOAuthProvider Provider { get; }

        public void Dispose() => _cache.Dispose();
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class ThrowingHandler(ExternalFailure failure) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(CreateException(failure));

        private static Exception CreateException(ExternalFailure failure) => failure switch
        {
            ExternalFailure.HttpRequest => new HttpRequestException("transport"),
            ExternalFailure.Io => new IOException("stream"),
            ExternalFailure.Json => new JsonException("json"),
            ExternalFailure.SecurityToken => new SecurityTokenException("token"),
            ExternalFailure.Argument => new ArgumentException("argument"),
            ExternalFailure.InvalidOperation => new InvalidOperationException("operation"),
            ExternalFailure.NotSupported => new NotSupportedException("unsupported"),
            ExternalFailure.UpstreamCancellation => new OperationCanceledException("upstream"),
            ExternalFailure.Unknown => new DivideByZeroException("programming failure"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
    }
}
