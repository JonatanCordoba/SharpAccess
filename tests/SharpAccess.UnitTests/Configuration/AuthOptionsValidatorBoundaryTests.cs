using SharpAccess.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthOptionsValidatorBoundaryTests
{
    [Fact]
    public void ValidatorAcceptsConfigurationWithEveryOptionalFeatureDisabled()
    {
        AuthOptions options = TestOptions.Create();
        DisableAllFeatures(options);
        options.JwtSigningKey = string.Empty;
        options.TokenHashing.Key = string.Empty;
        options.Passwords.Peppers.Clear();

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidatorAcceptsHostKeyRingWithoutConfiguredHmacMaterial()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = string.Empty;
        options.AccessTokenSigning.UseHostKeyRing = true;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    // Verifies that a zero-length hash queue remains valid while hashing capacity must stay positive.
    [Fact]
    public void ValidatorAcceptsAZeroLengthPasswordHashQueueButNotZeroHashCapacity()
    {
        AuthOptions options = TestOptions.Create();
        options.Passwords.MaximumQueuedPasswordHashes = 0;

        Assert.True(new AuthOptionsValidator(TestOptions.Clock).Validate(null, options).Succeeded);

        options.Passwords.MaximumConcurrentPasswordHashes = 0;
        ValidateOptionsResult invalid = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);
        Assert.False(invalid.Succeeded);
        Assert.Contains(
            invalid.Failures!,
            static failure => failure.Contains("MaximumConcurrentPasswordHashes", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorAcceptsDisabledBreachedPasswordEndpointWithoutHttps()
    {
        AuthOptions options = TestOptions.Create();
        options.Passwords.BreachedPasswords.Enabled = false;
        options.Passwords.BreachedPasswords.Endpoint = new UriBuilder(
            Uri.UriSchemeHttp,
            "breaches.example")
        {
            Path = "/range/"
        }.Uri;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidatorAcceptsValidMigrationScriptPath()
    {
        AuthOptions options = TestOptions.Create();
        options.Migrations.Mode = SharpAccessMigrationMode.GenerateScript;
        options.Migrations.ScriptOutputPath = "artifacts/migrations/sharpaccess.sql";

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(FeatureDependencyFailures))]
    public void ValidatorRejectsFeatureDependencyFailures(
        Action<AuthOptions> mutate,
        string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        DisableAllFeatures(options);
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(IntegerBoundaryFailures))]
    public void ValidatorRejectsIntegerBoundaryFailures(
        Action<AuthOptions> mutate,
        string expectedField)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedField, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TimeSpanBoundaryFailures))]
    public void ValidatorRejectsTimeSpanBoundaryFailures(
        Action<AuthOptions> mutate,
        string expectedField)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedField, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(IdentifierFailures))]
    public void ValidatorRejectsInvalidVersionIdentifiers(
        Action<AuthOptions> mutate,
        string expectedField)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedField, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UriAndCookieFailures))]
    public void ValidatorRejectsRemainingUriAndCookieBranches(
        Action<AuthOptions> mutate,
        string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    public static TheoryData<Action<AuthOptions>, string> FeatureDependencyFailures => new()
    {
        {
            options => options.Features.Registration = true,
            "Registration requires PasswordAuthentication"
        },
        {
            options => options.Features.PasswordReset = true,
            "PasswordReset requires PasswordAuthentication"
        },
        {
            options => options.Features.RefreshTokens = true,
            "RefreshTokens requires PasswordAuthentication or an enabled OpenIdConnect provider"
        },
        {
            options => options.Features.Administration = true,
            "Administration requires PasswordAuthentication or an enabled OpenIdConnect provider"
        },
        {
            options => options.Features.Tenancy = true,
            "Tenancy requires PasswordAuthentication or an enabled OpenIdConnect provider"
        }
    };

    public static TheoryData<Action<AuthOptions>, string> IntegerBoundaryFailures => new()
    {
        { options => options.AccessTokenMinutes = 0, "AccessTokenMinutes" },
        { options => options.AccessTokenMinutes = 1_441, "AccessTokenMinutes" },
        { options => options.FreshAuthenticationMinutes = 0, "FreshAuthenticationMinutes" },
        {
            options => options.FreshAuthenticationMinutes =
                options.AccessTokenMinutes + 1,
            "FreshAuthenticationMinutes"
        },
        { options => options.RefreshTokenDays = 0, "RefreshTokenDays" },
        { options => options.RefreshTokenDays = 366, "RefreshTokenDays" },
        { options => options.EmailVerificationMinutes = 4, "EmailVerificationMinutes" },
        { options => options.EmailVerificationMinutes = 10_081, "EmailVerificationMinutes" },
        { options => options.PasswordResetMinutes = 4, "PasswordResetMinutes" },
        { options => options.PasswordResetMinutes = 1_441, "PasswordResetMinutes" },
        {
            options =>
            {
                EnableGoogle(options);
                options.OAuthStateMinutes = 0;
            },
            "OAuthStateMinutes"
        },
        {
            options =>
            {
                EnableGoogle(options);
                options.OAuthStateMinutes = 61;
            },
            "OAuthStateMinutes"
        },
        {
            options =>
            {
                EnableGoogle(options);
                options.OAuthExchangeMinutes = 0;
            },
            "OAuthExchangeMinutes"
        },
        {
            options =>
            {
                EnableGoogle(options);
                options.OAuthExchangeMinutes = 11;
            },
            "OAuthExchangeMinutes"
        },
        { options => options.Passwords.MinimumLength = 7, "Passwords.MinimumLength" },
        { options => options.Passwords.MinimumLength = 129, "Passwords.MinimumLength" },
        {
            options => options.Passwords.MaximumLength =
                options.Passwords.MinimumLength - 1,
            "Passwords.MaximumLength"
        },
        { options => options.Passwords.MaximumLength = 1_025, "Passwords.MaximumLength" },
        { options => options.Passwords.Iterations = 0, "Passwords.Iterations" },
        { options => options.Passwords.Iterations = 11, "Passwords.Iterations" },
        { options => options.Passwords.MemorySizeKiB = 8_191, "Passwords.MemorySizeKiB" },
        { options => options.Passwords.MemorySizeKiB = 262_145, "Passwords.MemorySizeKiB" },
        { options => options.Passwords.DegreeOfParallelism = 0, "Passwords.DegreeOfParallelism" },
        { options => options.Passwords.DegreeOfParallelism = 33, "Passwords.DegreeOfParallelism" },
        { options => options.Passwords.SaltSizeBytes = 15, "Passwords.SaltSizeBytes" },
        { options => options.Passwords.SaltSizeBytes = 65, "Passwords.SaltSizeBytes" },
        { options => options.Passwords.HashSizeBytes = 31, "Passwords.HashSizeBytes" },
        { options => options.Passwords.HashSizeBytes = 65, "Passwords.HashSizeBytes" },
        {
            options => options.Passwords.MaximumConcurrentPasswordHashes = 0,
            "Passwords.MaximumConcurrentPasswordHashes"
        },
        {
            options => options.Passwords.MaximumConcurrentPasswordHashes = 257,
            "Passwords.MaximumConcurrentPasswordHashes"
        },
        {
            options => options.Passwords.MaximumQueuedPasswordHashes = -1,
            "Passwords.MaximumQueuedPasswordHashes"
        },
        {
            options => options.Passwords.MaximumQueuedPasswordHashes = 10_001,
            "Passwords.MaximumQueuedPasswordHashes"
        },
        { options => options.Lockout.FailedAttempts = 0, "Lockout.FailedAttempts" },
        { options => options.Lockout.FailedAttempts = 101, "Lockout.FailedAttempts" },
        { options => options.Lockout.Minutes = 0, "Lockout.Minutes" },
        { options => options.Lockout.Minutes = 10_081, "Lockout.Minutes" },
        { options => options.RateLimits.LoginPerMinute = 0, "RateLimits.LoginPerMinute" },
        { options => options.RateLimits.LoginPerMinute = 10_001, "RateLimits.LoginPerMinute" },
        { options => options.RateLimits.RegisterPerMinute = 0, "RateLimits.RegisterPerMinute" },
        {
            options => options.RateLimits.EmailVerificationPerMinute = 10_001,
            "RateLimits.EmailVerificationPerMinute"
        },
        { options => options.RateLimits.RefreshPerMinute = 0, "RateLimits.RefreshPerMinute" },
        {
            options => options.RateLimits.PasswordResetPerMinute = 10_001,
            "RateLimits.PasswordResetPerMinute"
        },
        {
            options =>
            {
                EnableGoogle(options);
                options.RateLimits.OAuthPerMinute = 0;
            },
            "RateLimits.OAuthPerMinute"
        },
        {
            options => options.SecurityLimits.MaximumRolesPerToken = 257,
            "SecurityLimits.MaximumRolesPerToken"
        },
        {
            options => options.SecurityLimits.MaximumPermissionsPerToken = 1_025,
            "SecurityLimits.MaximumPermissionsPerToken"
        },
        {
            options => options.SecurityLimits.MaximumEncodedAccessTokenBytes = 65_537,
            "SecurityLimits.MaximumEncodedAccessTokenBytes"
        },
        {
            options => options.SecurityLimits.MaximumActiveRefreshFamiliesPerUser = 1_001,
            "SecurityLimits.MaximumActiveRefreshFamiliesPerUser"
        },
        {
            options => options.SecurityLimits.MaximumActiveRefreshTokensPerFamily = 1_001,
            "SecurityLimits.MaximumActiveRefreshTokensPerFamily"
        },
        {
            options => options.Passwords.BreachedPasswords.CircuitBreakerFailureThreshold = 101,
            "CircuitBreakerFailureThreshold"
        },
        {
            options => options.Passwords.BreachedPasswords.MaximumCacheEntries = 100_001,
            "MaximumCacheEntries"
        },
        {
            options => options.Passwords.BreachedPasswords.MaximumResponseBytes = 8_388_609,
            "MaximumResponseBytes"
        }
    };

    public static TheoryData<Action<AuthOptions>, string> TimeSpanBoundaryFailures => new()
    {
        {
            options => options.Passwords.PasswordHashQueueTimeout =
                TimeSpan.FromMilliseconds(99),
            "PasswordHashQueueTimeout"
        },
        {
            options => options.Passwords.PasswordHashQueueTimeout =
                TimeSpan.FromMinutes(2).Add(TimeSpan.FromMilliseconds(1)),
            "PasswordHashQueueTimeout"
        },
        {
            options => options.Passwords.BreachedPasswords.Timeout =
                TimeSpan.FromMilliseconds(99),
            "Passwords.BreachedPasswords.Timeout"
        },
        {
            options => options.Passwords.BreachedPasswords.Timeout =
                TimeSpan.FromSeconds(31),
            "Passwords.BreachedPasswords.Timeout"
        },
        {
            options => options.Passwords.BreachedPasswords.CircuitBreakerDuration =
                TimeSpan.FromMilliseconds(999),
            "CircuitBreakerDuration"
        },
        {
            options => options.Passwords.BreachedPasswords.CircuitBreakerDuration =
                TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)),
            "CircuitBreakerDuration"
        },
        {
            options => options.Passwords.BreachedPasswords.CacheDuration =
                TimeSpan.FromSeconds(59),
            "CacheDuration"
        },
        {
            options => options.Passwords.BreachedPasswords.CacheDuration =
                TimeSpan.FromDays(7).Add(TimeSpan.FromSeconds(1)),
            "CacheDuration"
        }
    };

    public static TheoryData<Action<AuthOptions>, string> IdentifierFailures => new()
    {
        {
            options => options.Passwords.CurrentPepperVersion =
                new string('v', 129),
            "Passwords.Peppers must contain the current pepper version"
        },
        {
            options =>
            {
                options.Passwords.CurrentPepperVersion = "bad:version";
                options.Passwords.Peppers["bad:version"] =
                    options.Passwords.Peppers["v1"];
            },
            "Passwords.Peppers version"
        },
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.CurrentKeyVersion = "bad\u0001version";
                options.TokenHashing.LegacyUnversionedKeyVersion = null;
                options.TokenHashing.Keys["bad\u0001version"] =
                    "0123456789abcdef-0123456789abcdef";
            },
            "TokenHashing.CurrentKeyVersion"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "active";
                options.AccessTokenSigning.HmacSha256Keys[
                    new string('k', 129)] =
                    new HmacAccessTokenSigningKeyOptions
                    {
                        Key = "0123456789abcdef-0123456789abcdef"
                    };
                options.AccessTokenSigning.HmacSha256Keys["active"] =
                    new HmacAccessTokenSigningKeyOptions
                    {
                        Key = "fedcba9876543210-fedcba9876543210"
                    };
            },
            "AccessTokenSigning key identifier"
        }
    };

    public static TheoryData<Action<AuthOptions>, string> UriAndCookieFailures => new()
    {
        {
            options => options.BaseUri = new UriBuilder(
                Uri.UriSchemeHttp,
                "app.example").Uri,
            "must use HTTPS"
        },
        {
            options => options.BaseUri = new UriBuilder(
                Uri.UriSchemeHttps,
                "app.example")
            {
                UserName = "user",
                Password = "password"
            }.Uri,
            "cannot contain credentials"
        },
        {
            options =>
            {
                options.RefreshCookieSecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.BaseUri = new Uri("https://app.example");
            },
            "RefreshCookieSecurePolicy"
        },
        {
            options =>
            {
                EnableGoogle(options);
                TestOptions.Google(options).AuthorizationEndpoint = new UriBuilder(
                    Uri.UriSchemeHttps,
                    "oauth.example")
                {
                    Fragment = "fragment"
                }.Uri;
            },
            "AuthorizationEndpoint cannot contain"
        },
        {
            options =>
            {
                EnableGoogle(options);
                TestOptions.Google(options).TokenEndpoint = new UriBuilder(
                    Uri.UriSchemeHttps,
                    "oauth.example")
                {
                    Query = "mode=test"
                }.Uri;
            },
            "TokenEndpoint cannot contain"
        },
        {
            options =>
            {
                EnableGoogle(options);
                TestOptions.Google(options).JsonWebKeySetEndpoint = new UriBuilder(
                    Uri.UriSchemeHttps,
                    "oauth.example")
                {
                    UserName = "user"
                }.Uri;
            },
            "JsonWebKeySetEndpoint cannot contain"
        }
    };

    private static void EnableGoogle(AuthOptions options)
    {
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
    }

    private static void DisableAllFeatures(AuthOptions options)
    {
        options.Features.PasswordAuthentication = false;
        options.Features.Registration = false;
        options.Features.PasswordReset = false;
        options.Features.RefreshTokens = false;
        foreach (OpenIdConnectProviderOptions provider in options.OpenIdConnect.Providers.Values)
        {
            provider.Enabled = false;
        }
        options.Features.Administration = false;
        options.Features.Tenancy = false;
    }
}
