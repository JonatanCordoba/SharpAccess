using SharpAccess.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthOptionsValidatorDecompositionTests
{
    private static readonly DateTimeOffset Now = TestOptions.Now;
    private static readonly string StrongSecret = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());

    // Proves that decomposition preserves the established cross-section failure order.
    [Fact]
    public void ValidatePreservesFailureOrderingAcrossConfigurationSections()
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = new Uri("ftp://app.example");
        options.JwtIssuer = " ";
        options.SecurityLimits.MaximumRolesPerToken = 0;
        options.RefreshTokenDays = 0;
        options.EmailVerificationMinutes = 0;
        options.Passwords.MinimumLength = 0;
        options.TokenHashing.Key = "short";
        options.RateLimits.PartitionKey = "short";
        options.Lockout.FailedAttempts = 0;
        options.RateLimits.LoginPerMinute = 0;
        options.Migrations.Mode = (SharpAccessMigrationMode)99;

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        string[] failures = result.Failures!.ToArray();
        AssertFailureOrder(
            failures,
            "BaseUri must be an absolute HTTP or HTTPS URI",
            "JwtIssuer is required",
            "SecurityLimits.MaximumRolesPerToken",
            "RefreshTokenDays",
            "EmailVerificationMinutes",
            "Passwords.MinimumLength",
            "TokenHashing.Key",
            "RateLimits.PartitionKey",
            "Lockout.FailedAttempts",
            "RateLimits.LoginPerMinute",
            "Migrations.Mode");
    }

    // Accepts every printable non-separator character used by HTTP token names.
    [Fact]
    public void ValidateAcceptsCompleteHttpTokenAlphabet()
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = new Uri("http://localhost");
        options.RefreshTokenCookieName = "!#$%&'*+-.^_`|~09AZaz";
        options.CsrfHeaderName = "!#$%&'*+-.^_`|~09AZaz";

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    // Rejects every separator and non-printable boundary excluded from HTTP token names.
    [Theory]
    [MemberData(nameof(InvalidHttpTokenCharacters))]
    public void ValidateRejectsInvalidHttpTokenCharacters(char invalidCharacter)
    {
        AuthOptions options = TestOptions.Create();
        options.BaseUri = new Uri("http://localhost");
        options.RefreshTokenCookieName = $"a{invalidCharacter}b";
        options.CsrfHeaderName = $"a{invalidCharacter}b";

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("RefreshTokenCookieName", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("CsrfHeaderName", StringComparison.Ordinal));
    }

    // Rejects an active signing key whose activation time has not arrived.
    [Fact]
    public void ValidateRejectsFutureActiveSigningKeyActivation()
    {
        AuthOptions options = VersionedSigningOptions();
        options.AccessTokenSigning.HmacSha256Keys["current"].ActivatedUtc = Now.AddMinutes(1);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("activation time is in the future", StringComparison.Ordinal));
    }

    // Rejects an active signing key whose not-before time has not arrived.
    [Fact]
    public void ValidateRejectsFutureActiveSigningKeyNotBeforeTime()
    {
        AuthOptions options = VersionedSigningOptions();
        options.AccessTokenSigning.HmacSha256Keys["current"].NotBeforeUtc = Now.AddMinutes(1);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("not-before time is in the future", StringComparison.Ordinal));
    }

    // Rejects the migration-only single signing key in Production after validating its material.
    [Fact]
    public void ProductionRejectsLegacySingleSigningKey()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = StrongSecret;

        ValidateOptionsResult result = new AuthOptionsValidator(
            TestOptions.Clock,
            new ProductionEnvironment()).Validate(null, options);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("migration-only single-key", StringComparison.Ordinal));
    }

    // Rejects missing, oversized, and malformed explicit provider host allowlists.
    [Theory]
    [MemberData(nameof(InvalidAllowedHosts))]
    public void ValidateRejectsInvalidProviderHostAllowlists(IList<string>? allowedHosts)
    {
        AuthOptions options = EnabledOpenIdConnectOptions();
        options.OpenIdConnect.Providers["google"].AllowedHosts = allowedHosts!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("AllowedHosts", StringComparison.Ordinal));
    }

    // Requires each configured endpoint host to appear in the explicit allowlist.
    [Fact]
    public void ValidateRejectsEveryProviderEndpointOutsideHostAllowlist()
    {
        AuthOptions options = EnabledOpenIdConnectOptions();
        OpenIdConnectProviderOptions provider = options.OpenIdConnect.Providers["google"];
        provider.AllowedHosts = ["allowed.example"];

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        string[] hostFailures = result.Failures!
            .Where(static failure => failure.Contains("host must appear", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, hostFailures.Length);
    }

    // Accepts both exact HTTPS issuer URIs and the documented legacy DNS issuer form.
    [Theory]
    [InlineData("https://issuer.example")]
    [InlineData("accounts.example.com")]
    public void ValidateAcceptsSupportedProviderIssuerForms(string issuer)
    {
        AuthOptions options = EnabledOpenIdConnectOptions();
        options.OpenIdConnect.Providers["google"].ValidIssuers = [issuer];

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    // Rejects missing, oversized, malformed, or non-HTTPS provider issuers.
    [Theory]
    [MemberData(nameof(InvalidIssuers))]
    public void ValidateRejectsInvalidProviderIssuers(IList<string>? issuers)
    {
        AuthOptions options = EnabledOpenIdConnectOptions();
        options.OpenIdConnect.Providers["google"].ValidIssuers = issuers!;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("ValidIssuers", StringComparison.Ordinal));
    }

    public static TheoryData<char> InvalidHttpTokenCharacters => new()
    {
        ' ', '\t', '\u001f', '\u007f', '\u0080',
        '(', ')', '<', '>', '@', ',', ';', ':', '\\', '"',
        '/', '[', ']', '?', '=', '{', '}'
    };

    public static TheoryData<IList<string>?> InvalidAllowedHosts => new()
    {
        (IList<string>?)null,
        new List<string>(),
        Enumerable.Range(0, 17).Select(static index => $"host-{index}.example").ToList(),
        new List<string> { " bad.example" },
        new List<string> { "bad.example/path" },
        new List<string> { "bad.example:443" },
        new List<string> { "not a host" }
    };

    public static TheoryData<IList<string>?> InvalidIssuers => new()
    {
        (IList<string>?)null,
        new List<string>(),
        Enumerable.Range(0, 9).Select(static index => $"https://issuer-{index}.example").ToList(),
        new List<string> { " " },
        new List<string> { new string('a', 513) },
        new List<string> { " issuer.example" },
        new List<string> { "issuer\u0001.example" },
        new List<string> { string.Concat(Uri.UriSchemeHttp, "://issuer.example") },
        new List<string> { "https://issuer.example?query=1" },
        new List<string> { "https://issuer.example#fragment" },
        new List<string> { "https://user@issuer.example" }
    };

    // Creates an otherwise valid versioned signing-ring configuration.
    private static AuthOptions VersionedSigningOptions()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = string.Empty;
        options.AccessTokenSigning.ActiveKeyId = "current";
        options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
        {
            Key = StrongSecret,
            ActivatedUtc = Now.AddMinutes(-1)
        };
        return options;
    }

    // Creates an otherwise valid enabled OpenID Connect provider configuration.
    private static AuthOptions EnabledOpenIdConnectOptions()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions provider = TestOptions.EnableGoogle(options);
        provider.ClientId = "client-id";
        provider.ClientSecret = "client-secret-value";
        provider.AuthorizationEndpoint = new Uri("https://oauth.example/authorize");
        provider.TokenEndpoint = new Uri("https://oauth.example/token");
        provider.JsonWebKeySetEndpoint = new Uri("https://oauth.example/jwks");
        provider.ValidIssuers = ["https://oauth.example"];
        provider.AllowedHosts = ["oauth.example"];
        return options;
    }

    // Asserts that selected failure fragments retain their established relative order.
    private static void AssertFailureOrder(string[] failures, params string[] expectedFragments)
    {
        int previousIndex = -1;
        foreach (string fragment in expectedFragments)
        {
            int currentIndex = Array.FindIndex(
                failures,
                failure => failure.Contains(fragment, StringComparison.Ordinal));
            Assert.True(currentIndex > previousIndex, $"Expected '{fragment}' after index {previousIndex}.");
            previousIndex = currentIndex;
        }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = nameof(AuthOptionsValidatorDecompositionTests);

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
