using System.Text;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthOptionsValidatorBranchTests
{
    [Fact]
    public void ValidateRejectsNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new AuthOptionsValidator().Validate(null, null!));
    }

    [Theory]
    [MemberData(nameof(NullNestedOptions))]
    public void ValidateRejectsNullNestedOptions(Action<AuthOptions> mutate)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("Nested authentication option objects", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidBaseUris))]
    public void ValidateRejectsInvalidBaseUris(Uri? baseUri, string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = baseUri!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad cookie")]
    [InlineData("bad,cookie")]
    public void ValidateRejectsInvalidRefreshCookieNames(string? cookieName)
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookieName = cookieName!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RefreshTokenCookieName", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsOversizedRefreshCookieName()
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookieName = new string('a', 129);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RefreshTokenCookieName", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("X Bad")]
    [InlineData("X:Bad")]
    public void ValidateRejectsInvalidCsrfHeaderNames(string? headerName)
    {
        AuthOptions options = TestOptions.Create();
        options.CsrfHeaderName = headerName!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("CsrfHeaderName", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsOversizedCsrfHeaderName()
    {
        AuthOptions options = TestOptions.Create();
        options.CsrfHeaderName = new string('a', 129);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("CsrfHeaderName", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsEmptyCsrfHeaderValue()
    {
        AuthOptions options = TestOptions.Create();
        options.CsrfHeaderValue = " ";

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("CsrfHeaderValue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("//auth")]
    [InlineData("/auth\\refresh")]
    [InlineData("/auth?x=1")]
    [InlineData("/auth#refresh")]
    [InlineData("/auth;refresh")]
    [InlineData("/auth\u0001")]
    public void ValidateRejectsInvalidRefreshCookiePaths(string? path)
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookiePath = path!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RefreshTokenCookiePath", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsOversizedRefreshCookiePath()
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookiePath = "/" + new string('a', 1_024);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RefreshTokenCookiePath", StringComparison.Ordinal));
    }

    [Fact]
    public void NonLoopbackRefreshTokensRequireCsrfHeaderAndSecureCookiePrefix()
    {
        AuthOptions options = TestOptions.Create();
        options.RequireCsrfHeaderForCookieRefreshRequests = false;
        options.RefreshTokenCookieName = "dotnet_auth_refresh";
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.Always;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RequireCsrfHeaderForCookieRefreshRequests", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("__Secure-", StringComparison.Ordinal));
    }

    [Fact]
    public void Base64SecretsAreAcceptedWhenLongEnough()
    {
        string secret = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 32)));
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = secret;
        options.TokenHashing.Key = secret;
        options.Passwords.Peppers["v1"] = secret;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ShortBase64SecretsAreRejected()
    {
        string secret = Convert.ToBase64String(Encoding.UTF8.GetBytes("short"));
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = secret;
        options.TokenHashing.Key = secret;
        options.Passwords.Peppers["v1"] = secret;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("JwtSigningKey", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("TokenHashing.Key", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("Passwords.Peppers", StringComparison.Ordinal));
    }

    [Fact]
    public void NullPepperDictionaryIsRejected()
    {
        AuthOptions options = TestOptions.Create();
        options.Passwords.Peppers = null!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("Passwords.Peppers cannot be null", StringComparison.Ordinal));
    }

    // Verifies that malformed OpenID Connect endpoints fail configuration validation.
    [Theory]
    [MemberData(nameof(InvalidOpenIdConnectEndpoints))]
    public void ValidateRejectsInvalidOpenIdConnectEndpoints(
        Action<OpenIdConnectProviderOptions> mutate,
        string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        mutate(google);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains(expectedFailure, StringComparison.Ordinal));
    }

    // Verifies that OpenID Connect callback paths stay absolute and free of unsafe components.
    [Theory]
    [InlineData("/auth/oauth/google/callback", true)]
    [InlineData("auth/oauth/google/callback", false)]
    [InlineData("/auth/oauth/google/callback?x=1", false)]
    [InlineData("/auth/oauth/google/callback#x", false)]
    [InlineData("/auth/oauth/google/callback;bad", false)]
    [InlineData("/auth/oauth/google/\u0001", false)]
    public void ValidateChecksOpenIdConnectCallbackPath(string callbackPath, bool expectedSuccess)
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        google.CallbackPath = callbackPath;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    // Verifies that refresh tokens may accompany either supported interactive sign-in mode.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RefreshTokensCanDependOnEitherInteractiveSignInOption(
        bool passwordAuthentication,
        bool openIdConnect)
    {
        AuthOptions options = TestOptions.Create();
        options.Features.PasswordAuthentication = passwordAuthentication;
        options.Features.Registration = passwordAuthentication;
        options.Features.PasswordReset = passwordAuthentication;
        OpenIdConnectProviderOptions google = TestOptions.Google(options);
        google.Enabled = openIdConnect;
        google.ClientId = openIdConnect ? "client-id" : string.Empty;
        google.ClientSecret = openIdConnect ? "client-secret-value" : string.Empty;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    public static TheoryData<Action<AuthOptions>> NullNestedOptions => new()
    {
        options => options.Features = null!,
        options => options.Passwords = null!,
        options => options.TokenHashing = null!,
        options => options.Lockout = null!,
        options => options.RateLimits = null!,
        options => options.OpenIdConnect = null!
    };

    public static TheoryData<Uri?, string> InvalidBaseUris => new()
    {
        { null, "BaseUri" },
        { new Uri("/relative", UriKind.Relative), "BaseUri" },
        { new Uri("ftp://app.test"), "BaseUri" },
        { new Uri("https://app.test?x=1"), "BaseUri cannot contain" },
        { new Uri("https://app.test#fragment"), "BaseUri cannot contain" }
    };

    public static TheoryData<Action<OpenIdConnectProviderOptions>, string> InvalidOpenIdConnectEndpoints => new()
    {
        { options => options.AuthorizationEndpoint = null!, "AuthorizationEndpoint" },
        { options => options.TokenEndpoint = null!, "TokenEndpoint" },
        { options => options.JsonWebKeySetEndpoint = null!, "JsonWebKeySetEndpoint" },
        { options => options.TokenEndpoint = new Uri("https://user:pass@oauth.example/token"), "TokenEndpoint" },
        { options => options.JsonWebKeySetEndpoint = new Uri("https://oauth.example/certs?x=1"), "JsonWebKeySetEndpoint" }
    };
}
