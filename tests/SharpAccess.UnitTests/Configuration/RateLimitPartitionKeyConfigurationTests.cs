using SharpAccess.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class RateLimitPartitionKeyConfigurationTests
{
    // Verifies every rate-limited feature requires dedicated strong partition material.
    [Theory]
    [MemberData(nameof(EnabledFeatureMatrix))]
    public void EveryMappedRateLimitedFeatureRequiresItsOwnStrongPartitionKey(
        Action<AuthOptions> enable)
    {
        AuthOptions options = BaselineWithNoEnabledFeatures();
        enable(options);
        options.RateLimits.PartitionKey = string.Empty;

        ValidateOptionsResult missing = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(missing.Succeeded);
        Assert.Contains(
            missing.Failures!,
            static failure => failure.Contains("RateLimits.PartitionKey is required", StringComparison.Ordinal));

        options.RateLimits.PartitionKey = StrongSecret(201);
        ValidateOptionsResult configured = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.True(configured.Succeeded, string.Join(Environment.NewLine, configured.Failures ?? []));
    }

    // Verifies a fully disabled endpoint set does not require unused partition material.
    [Fact]
    public void DisabledFeaturesAndDisabledProviderEntriesDoNotRequirePartitionMaterial()
    {
        AuthOptions options = new();
        options.RateLimits.LoginPerMinute = 0;
        options.RateLimits.RegisterPerMinute = 0;
        options.RateLimits.RefreshPerMinute = 0;
        options.RateLimits.PasswordResetPerMinute = 0;
        options.RateLimits.EmailVerificationPerMinute = 0;
        options.RateLimits.OAuthPerMinute = 0;

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.True(result.Succeeded);
        Assert.False(options.OpenIdConnect.Providers["google"].Enabled);
    }

    // Verifies rate limiting cannot reuse any other configured cryptographic purpose.
    [Theory]
    [MemberData(nameof(ReusedSecretMatrix))]
    public void PartitionKeyCannotReuseAnyOtherSecret(
        Action<AuthOptions> arrange,
        Func<AuthOptions, string> selectReusedSecret)
    {
        AuthOptions options = TestOptions.Create();
        arrange(options);
        options.RateLimits.PartitionKey = selectReusedSecret(options);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("must be dedicated", StringComparison.Ordinal));
    }

    // Verifies host-managed signing keys do not weaken partition-key isolation.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionHostKeyRingsStillRequireIndependentPartitionMaterial(bool externalOnly)
    {
        AuthOptions options = BaselineWithNoEnabledFeatures();
        options.AccessTokenSigning.UseHostKeyRing = true;
        options.AccessTokenSigning.HmacSha256Keys.Clear();
        options.JwtSigningKey = string.Empty;
        options.Passwords.Peppers["v1"] = StrongSecret(1);
        options.TokenHashing.Key = StrongSecret(41);
        options.RateLimits.PartitionKey = StrongSecret(81);
        if (externalOnly)
        {
            EnableGoogle(options);
        }
        else
        {
            options.Features.PasswordAuthentication = true;
        }

        ValidateOptionsResult result = new AuthOptionsValidator(
            TestOptions.Clock,
            new ProductionEnvironment()).Validate(null, options);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Failures ?? []));
    }

    // Verifies the runtime provider fails closed instead of falling back to another secret.
    [Fact]
    public void RuntimePartitionProviderNeverFallsBackToTokenHashingOrJwtSigningMaterial()
    {
        AuthOptions options = TestOptions.Create();
        options.RateLimits.PartitionKey = string.Empty;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new AuthRateLimitPartitionKeyProvider(Options.Create(options)));

        Assert.Contains("rate-limit partition key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Verifies the decoded partition key meets the minimum strength in every environment.
    [Fact]
    public void ShortPartitionMaterialIsRejectedInEveryEnvironment()
    {
        AuthOptions options = TestOptions.Create();
        options.RateLimits.PartitionKey = Convert.ToBase64String([1, 2, 3, 4]);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("at least 32 bytes", StringComparison.Ordinal));
    }

    // Verifies copied sample placeholders cannot become production partition secrets.
    [Theory]
    [InlineData("replace-with-a-dedicated-32-byte-random-key")]
    [InlineData("development-only-rate-limit-partition-key")]
    public void ProductionRejectsSamplePartitionPlaceholders(string partitionKey)
    {
        AuthOptions options = TestOptions.Create();
        options.RateLimits.PartitionKey = partitionKey;

        ValidateOptionsResult result = new AuthOptionsValidator(
            TestOptions.Clock,
            new ProductionEnvironment()).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            static failure => failure.Contains("predictable secret material", StringComparison.Ordinal));
    }

    public static TheoryData<Action<AuthOptions>> EnabledFeatureMatrix => new()
    {
        options => options.Features.PasswordAuthentication = true,
        options =>
        {
            options.Features.PasswordAuthentication = true;
            options.Features.Registration = true;
        },
        options =>
        {
            options.Features.PasswordAuthentication = true;
            options.Features.PasswordReset = true;
        },
        options =>
        {
            options.Features.PasswordAuthentication = true;
            options.Features.RefreshTokens = true;
        },
        EnableGoogle,
        options =>
        {
            options.Features.PasswordAuthentication = true;
            options.Features.Registration = true;
            options.Features.PasswordReset = true;
            options.Features.RefreshTokens = true;
            EnableGoogle(options);
        }
    };

    public static TheoryData<Action<AuthOptions>, Func<AuthOptions, string>> ReusedSecretMatrix => new()
    {
        { static _ => { }, static options => options.JwtSigningKey },
        { static _ => { }, static options => options.TokenHashing.Key },
        { static _ => { }, static options => options.Passwords.Peppers["v1"] },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "current";
                options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
                {
                    Key = StrongSecret(121),
                    ActivatedUtc = TestOptions.Now.AddMinutes(-1)
                };
            },
            static options => options.AccessTokenSigning.HmacSha256Keys["current"].Key
        },
        {
            EnableGoogle,
            static options => options.OpenIdConnect.Providers["google"].ClientSecret
        }
    };

    // Creates the valid baseline used to isolate feature-matrix behavior.
    private static AuthOptions BaselineWithNoEnabledFeatures()
    {
        AuthOptions options = TestOptions.Create();
        options.Features.PasswordAuthentication = false;
        options.Features.Registration = false;
        options.Features.PasswordReset = false;
        options.Features.RefreshTokens = false;
        options.Features.Administration = false;
        options.Features.Tenancy = false;
        foreach (OpenIdConnectProviderOptions provider in options.OpenIdConnect.Providers.Values)
        {
            provider.Enabled = false;
        }

        return options;
    }

    // Enables the disabled Google-compatible provider with valid test credentials.
    private static void EnableGoogle(AuthOptions options)
    {
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "google-client";
        google.ClientSecret = StrongSecret(161);
    }

    // Creates deterministic distinct 32-byte test material.
    private static string StrongSecret(int start) =>
        Convert.ToBase64String(
            Enumerable.Range(start, 32)
                .Select(static value => unchecked((byte)value))
                .ToArray());

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "SharpAccess.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
