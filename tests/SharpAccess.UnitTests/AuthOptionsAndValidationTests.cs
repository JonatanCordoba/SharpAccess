using SharpAccess.Attributes;
using SharpAccess.Configuration;
using SharpAccess.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthOptionsAndValidationTests
{
    // Verifies that valid options pass startup validation.
    [Fact]
    public void ValidOptionsPassStartupValidation()
    {
        AuthOptions options = TestOptions.Create();
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.True(result.Succeeded);
    }

    // Verifies that package defaults use only stable SharpAccess runtime identifiers.
    [Fact]
    public void RuntimeIdentifierDefaultsUseSharpAccessNames()
    {
        AuthOptions options = new();
        Assert.Equal("SharpAccess", options.JwtIssuer);
        Assert.Equal("SharpAccess.Clients", options.JwtAudience);
        Assert.Equal(AuthConstants.DefaultRefreshTokenCookieName, options.RefreshTokenCookieName);
        Assert.Equal(AuthConstants.DefaultCsrfHeaderName, options.CsrfHeaderName);
        Assert.Equal("SharpAccess.Jwt", AuthConstants.AuthenticationScheme);
    }

    // Verifies that missing security secrets fail validation.
    [Fact]
    public void MissingSecuritySecretsFailValidation()
    {
        AuthOptions options = new();
        options.Features.PasswordAuthentication = true;
        options.Features.RefreshTokens = true;
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("JwtSigningKey", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("TokenHashing.Key", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("current pepper", StringComparison.Ordinal));
    }

    // Verifies that the built-in Google-compatible OIDC entry requires confidential client configuration.
    [Fact]
    public void GoogleRequiresConfidentialClientConfiguration()
    {
        AuthOptions options = TestOptions.Create();
        TestOptions.EnableGoogle(options);
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("OpenIdConnect.Providers[google].ClientId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("OpenIdConnect.Providers[google].ClientSecret", StringComparison.Ordinal));
    }

    // Verifies that registration requires password authentication.
    [Fact]
    public void RegistrationRequiresPasswordAuthentication()
    {
        AuthOptions options = TestOptions.Create();
        options.Features.PasswordAuthentication = false;
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("Registration requires", StringComparison.Ordinal));
    }

    // Verifies that email validation is strict.
    [Theory]
    [InlineData("person@example.com", true)]
    [InlineData("Person <person@example.com>", false)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void EmailValidationIsStrict(string email, bool expected)
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));
        Assert.Equal(expected, validator.TryValidateEmail(email, out _));
    }

    // Verifies that password policy requires length letters and digits.
    [Theory]
    [InlineData("ValidPassword123", true)]
    [InlineData("alllettersarehere", false)]
    [InlineData("123456789012345", false)]
    [InlineData("Short1", false)]
    public void PasswordPolicyRequiresLengthLettersAndDigits(string password, bool expected)
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));
        Assert.Equal(expected, validator.IsValidPassword(password));
    }

    // Verifies that o auth return urls must be local.
    [Theory]
    [InlineData("/dashboard", true)]
    [InlineData("/", true)]
    [InlineData("//outside.example", false)]
    [InlineData("https://outside.example", false)]
    [InlineData("/bad\\path", false)]
    public void OAuthReturnUrlsMustBeLocal(string value, bool expected)
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));
        Assert.Equal(expected, validator.TryValidateReturnUrl(value, out _));
    }

    // Verifies that tenant slugs are normalized and restricted.
    [Theory]
    [InlineData("valid-slug", true)]
    [InlineData("Valid Slug", false)]
    [InlineData("-invalid", false)]
    public void TenantSlugsAreNormalizedAndRestricted(string value, bool expected)
    {
        InputValidator validator = new(Options.Create(TestOptions.Create()));
        Assert.Equal(expected, validator.TryValidateSlug(value, out _));
    }

    // Verifies that the explicit global-role attribute rejects comma-delimited names.
    [Fact]
    public void GlobalRoleAttributeRejectsCommaDelimitedNames()
    {
        Assert.Throws<ArgumentException>(() => new RequireGlobalRoleAttribute("Admin,User"));
        RequireGlobalRoleAttribute attribute = new("Admin", "Manager");
        Assert.Equal(AuthConstants.AuthenticationScheme, attribute.AuthenticationSchemes);
        Assert.Equal("Admin,Manager", attribute.Roles);
    }

    // Verifies that explicit global-permission attributes reject empty sets.
    [Fact]
    public void GlobalPermissionAttributesRejectEmptySets()
    {
        Assert.Throws<ArgumentException>(() => new RequireAnyGlobalPermissionAttribute());
        Assert.Throws<ArgumentException>(() => new RequireAllGlobalPermissionsAttribute(""));
        Assert.Throws<ArgumentException>(() => new RequireGlobalPermissionAttribute(" "));
    }

    // Verifies that refresh cookie path must be absolute.
    [Fact]
    public void RefreshCookiePathMustBeAbsolute()
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookiePath = "auth";
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
    }

    // Verifies that disabled features do not require unused secrets.
    [Fact]
    public void DisabledFeaturesDoNotRequireUnusedSecrets()
    {
        AuthOptions options = new();
        Assert.True(new AuthOptionsValidator().Validate(null, options).Succeeded);
    }

    // Verifies that insecure cookie policy can be explicitly selected for local hosts.
    [Fact]
    public void InsecureCookiePolicyCanBeExplicitlySelectedForLocalHosts()
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = new Uri("http://localhost:5000");
        options.RefreshTokenCookieName = AuthConstants.DefaultRefreshTokenCookieName;
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.RequireCsrfHeaderForCookieRefreshRequests = false;
        Assert.True(new AuthOptionsValidator().Validate(null, options).Succeeded);
    }

    // Verifies that non loopback hosts require https and secure cookies.
    [Fact]
    public void NonLoopbackHostsRequireHttpsAndSecureCookies()
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = new Uri("http://app.test"); // DevSkim: ignore DS137138 -- Intentional rejection fixture.
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.SameAsRequest;
        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("RefreshCookieSecurePolicy", StringComparison.Ordinal));
    }

    // Verifies that every feature is disabled until the host explicitly enables it.
    [Fact]
    public void FeatureGroupsAreOptInByDefault()
    {
        AuthFeatureOptions features = new();
        Assert.False(features.PasswordAuthentication);
        Assert.False(features.Registration);
        Assert.False(features.PasswordReset);
        Assert.False(features.RefreshTokens);
        Assert.False(features.Administration);
        Assert.False(features.Tenancy);
    }

    // Verifies that disabled feature settings do not create unrelated startup failures.
    [Fact]
    public void DisabledFeatureSettingsAreNotValidated()
    {
        AuthOptions options = new()
        {
            AccessTokenMinutes = 0,
            RefreshTokenDays = 0,
            EmailVerificationMinutes = 0,
            PasswordResetMinutes = 0,
            OAuthStateMinutes = 0,
            OAuthExchangeMinutes = 0,
            FreshAuthenticationMinutes = 0,
            RefreshTokenCookieName = "bad cookie",
            RefreshTokenCookiePath = "relative"
        };
        options.RateLimits.LoginPerMinute = 0;
        options.RateLimits.RegisterPerMinute = 0;
        options.RateLimits.RefreshPerMinute = 0;
        options.RateLimits.PasswordResetPerMinute = 0;
        options.RateLimits.EmailVerificationPerMinute = 0;
        options.RateLimits.OAuthPerMinute = 0;

        Assert.True(new AuthOptionsValidator().Validate(null, options).Succeeded);
    }

    // Verifies that cookie names, local callback paths, and provider endpoints reject delimiter injection.
    [Fact]
    public void SecuritySensitiveUrisAndCookieNamesAreStrict()
    {
        AuthOptions options = TestOptions.Create();
        options.RefreshTokenCookieName = "refresh;Path=/";
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        google.CallbackPath = "/auth/oauth/google/callback#fragment";
        google.TokenEndpoint = new Uri("https://oauth2.googleapis.com/token?unexpected=true");
        google.AuthorizationEndpoint = new Uri("https://accounts.google.com/o/oauth2/v2/auth");
        google.JsonWebKeySetEndpoint = new Uri("https://www.googleapis.com/oauth2/v3/certs");

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, value => value.Contains("RefreshTokenCookieName", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("CallbackPath", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, value => value.Contains("TokenEndpoint", StringComparison.Ordinal));
    }
}
