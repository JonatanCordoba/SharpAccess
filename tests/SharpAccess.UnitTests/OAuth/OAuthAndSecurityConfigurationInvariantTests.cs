using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.OAuth;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using SharpAccess.Tokens;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class OAuthAndSecurityConfigurationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly RequestMetadata Metadata = new("192.0.2.10", "configuration-invariant");
    private static readonly string StrongSecret = Convert.ToBase64String(
        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray());

    [Theory]
    [MemberData(nameof(NullSecurityNestedOptions))]
    public void ValidatorRejectsNullSecurityNestedOptions(Action<AuthOptions> mutate)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("Nested authentication option objects", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidSigningOptions))]
    public void ValidatorRejectsInvalidSigningRings(Action<AuthOptions> mutate, string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator(TestOptions.Clock).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidTokenHashingOptions))]
    public void ValidatorRejectsInvalidVersionedTokenHashing(Action<AuthOptions> mutate, string expectedFailure)
    {
        AuthOptions options = TestOptions.Create();
        mutate(options);

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsSecurityAndBreachedPasswordBounds()
    {
        AuthOptions options = TestOptions.Create();
        options.SecurityLimits.MaximumRolesPerToken = 0;
        options.SecurityLimits.MaximumPermissionsPerToken = 0;
        options.SecurityLimits.MaximumEncodedAccessTokenBytes = 0;
        options.SecurityLimits.MaximumActiveRefreshFamiliesPerUser = 0;
        options.SecurityLimits.MaximumActiveRefreshTokensPerFamily = 0;
        options.Passwords.BreachedPasswords.Enabled = true;
        options.Passwords.BreachedPasswords.Endpoint = new UriBuilder(
            Uri.UriSchemeHttp,
            "breaches.example")
        {
            Path = "/range/"
        }.Uri;
        options.Passwords.BreachedPasswords.FailureMode = (BreachedPasswordFailureMode)99;
        options.Passwords.BreachedPasswords.Timeout = TimeSpan.Zero;
        options.Passwords.BreachedPasswords.CircuitBreakerFailureThreshold = 0;
        options.Passwords.BreachedPasswords.CircuitBreakerDuration = TimeSpan.Zero;
        options.Passwords.BreachedPasswords.MaximumCacheEntries = 0;
        options.Passwords.BreachedPasswords.MaximumResponseBytes = 0;
        options.Passwords.BreachedPasswords.CacheDuration = TimeSpan.Zero;

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumRolesPerToken", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumPermissionsPerToken", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumEncodedAccessTokenBytes", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumActiveRefreshFamiliesPerUser", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumActiveRefreshTokensPerFamily", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("FailureMode", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("Endpoint", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("Timeout", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("CircuitBreakerFailureThreshold", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("CircuitBreakerDuration", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumCacheEntries", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("MaximumResponseBytes", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("CacheDuration", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorCoversMigrationModeAndScriptPathFailures()
    {
        AuthOptions invalidMode = TestOptions.Create();
        invalidMode.Migrations.Mode = (SharpAccessMigrationMode)99;
        ValidateOptionsResult invalidModeResult = new AuthOptionsValidator().Validate(null, invalidMode);
        Assert.Contains(
            invalidModeResult.Failures!,
            failure => failure.Contains("Migrations.Mode", StringComparison.Ordinal));

        AuthOptions missingPath = TestOptions.Create();
        missingPath.Migrations.Mode = SharpAccessMigrationMode.GenerateScript;
        ValidateOptionsResult missingPathResult = new AuthOptionsValidator().Validate(null, missingPath);
        Assert.Contains(
            missingPathResult.Failures!,
            failure => failure.Contains("ScriptOutputPath is required", StringComparison.Ordinal));

        AuthOptions oversizedPath = TestOptions.Create();
        oversizedPath.Migrations.Mode = SharpAccessMigrationMode.GenerateScript;
        oversizedPath.Migrations.ScriptOutputPath = new string('a', 4_097);
        ValidateOptionsResult oversizedPathResult = new AuthOptionsValidator().Validate(null, oversizedPath);
        Assert.Contains(
            oversizedPathResult.Failures!,
            failure => failure.Contains("valid path", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorAcceptsHostKeyRingAndCompleteVersionedHashing()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = string.Empty;
        options.AccessTokenSigning.UseHostKeyRing = true;
        options.TokenHashing.Key = string.Empty;
        options.TokenHashing.CurrentKeyVersion = "v2";
        options.TokenHashing.LegacyUnversionedKeyVersion = "v1";
        options.TokenHashing.Keys["v1"] = StrongSecret;
        options.TokenHashing.Keys["v2"] = Convert.ToBase64String(
            Enumerable.Range(33, 32).Select(static value => (byte)value).ToArray());

        ValidateOptionsResult result = new AuthOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ProductionValidatorRejectsSecretReuse()
    {
        AuthOptions options = TestOptions.Create();
        options.JwtSigningKey = string.Empty;
        options.AccessTokenSigning.ActiveKeyId = "current";
        options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
        {
            Key = StrongSecret,
            ActivatedUtc = Now.AddDays(-1)
        };
        options.Passwords.Peppers["v1"] = StrongSecret;
        options.TokenHashing.Key = string.Empty;
        options.TokenHashing.Keys["v1"] = StrongSecret;
        options.RateLimits.PartitionKey = StrongSecret;

        ValidateOptionsResult result = new AuthOptionsValidator(
            new TestHostEnvironment(Environments.Production)).Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains("reuse secret material", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OAuthChallengeValidatesProviderAndReturnUrl()
    {
        OAuthFixture fixture = CreateOAuthFixture();

        ServiceResult<Uri> unknown = await fixture.Service.CreateChallengeAsync(
            "unknown",
            "/return",
            Metadata);
        Assert.Equal(AuthError.Disabled, unknown.Error);

        fixture.Provider.IsEnabled = false;
        ServiceResult<Uri> disabled = await fixture.Service.CreateChallengeAsync(
            "google",
            "/return",
            Metadata);
        Assert.Equal(AuthError.Disabled, disabled.Error);

        fixture.Provider.IsEnabled = true;
        ServiceResult<Uri> invalidReturn = await fixture.Service.CreateChallengeAsync(
            "google",
            "https://evil.example/",
            Metadata);
        Assert.Equal(AuthError.InvalidInput, invalidReturn.Error);
    }

    [Fact]
    public async Task OAuthChallengePersistsProtectedPkceState()
    {
        OAuthFixture fixture = CreateOAuthFixture();

        ServiceResult<Uri> result = await fixture.Service.CreateChallengeAsync(
            "google",
            "/after-login",
            Metadata);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.NotNull(fixture.Store.SavedState);
        Assert.Equal("google", fixture.Store.SavedState.Provider);
        Assert.Equal("hash:state-value", fixture.Store.SavedState.StateHash);
        Assert.Equal("/after-login", fixture.Store.SavedState.ReturnUrl);
        Assert.NotEqual("verifier-value", fixture.Store.SavedState.ProtectedCodeVerifier);
        Assert.Equal("state-value", fixture.Provider.State);
        Assert.Equal("nonce-value", fixture.Provider.Nonce);
        Assert.NotNull(fixture.Provider.CodeChallenge);
        Assert.NotEmpty(fixture.Provider.CodeChallenge);
    }

    [Fact]
    public async Task OAuthCallbackRejectsInvalidOrMissingState()
    {
        OAuthFixture fixture = CreateOAuthFixture();

        ServiceResult<Uri> unknown = await fixture.Service.HandleCallbackAsync(
            "unknown",
            "code",
            "state",
            null,
            Metadata);
        Assert.Equal(AuthError.Disabled, unknown.Error);

        ServiceResult<Uri> missing = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            null,
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, missing.Error);

        ServiceResult<Uri> oversized = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            new string('s', 1_025),
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, oversized.Error);

        ServiceResult<Uri> notStored = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, notStored.Error);
        Assert.Equal(2, fixture.Store.ConsumedStateHashes.Count);
        Assert.Contains("legacy:state-value", fixture.Store.ConsumedStateHashes);
        Assert.Contains("hash:state-value", fixture.Store.ConsumedStateHashes);
    }

    [Theory]
    [InlineData("access_denied", "code")]
    [InlineData(null, null)]
    [InlineData(null, "oversized")]
    public async Task OAuthCallbackRejectsProviderAuthorizationFailures(string? error, string? codeMode)
    {
        OAuthFixture fixture = await PrepareCallbackAsync();
        string? code = codeMode switch
        {
            null => null,
            "oversized" => new string('c', 4_097),
            _ => codeMode
        };

        ServiceResult<Uri> result = await fixture.Service.HandleCallbackAsync(
            "google",
            code,
            "state-value",
            error,
            Metadata);

        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal("oauth_callback_failed", result.Code);
    }

    [Fact]
    public async Task OAuthCallbackRejectsInvalidPayloadAndIdentity()
    {
        OAuthFixture invalidPayload = await PrepareCallbackAsync();
        invalidPayload.Store.StateToConsume = invalidPayload.Store.StateToConsume! with
        {
            ProtectedCodeVerifier = "not-protected"
        };
        ServiceResult<Uri> invalidPayloadResult = await invalidPayload.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, invalidPayloadResult.Error);

        OAuthFixture missingIdentity = await PrepareCallbackAsync();
        missingIdentity.Provider.Identity = null;
        ServiceResult<Uri> missingIdentityResult = await missingIdentity.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, missingIdentityResult.Error);

        OAuthFixture unverifiedIdentity = await PrepareCallbackAsync();
        unverifiedIdentity.Provider.Identity = new OAuthProviderIdentity(
            "subject",
            "person@example.com",
            EmailVerified: false,
            "Person");
        ServiceResult<Uri> unverifiedResult = await unverifiedIdentity.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, unverifiedResult.Error);

        OAuthFixture invalidEmail = await PrepareCallbackAsync();
        invalidEmail.Provider.Identity = new OAuthProviderIdentity(
            "subject",
            "not-an-email",
            EmailVerified: true,
            "Person");
        ServiceResult<Uri> invalidEmailResult = await invalidEmail.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, invalidEmailResult.Error);
    }

    // Verifies that OAuth identities exceeding persistence bounds are rejected before store mutation.
    [Theory]
    [InlineData("subject")]
    [InlineData("email")]
    [InlineData("display_name")]
    public async Task OAuthCallbackRejectsIdentityClaimsOutsidePersistenceBounds(string claim)
    {
        OAuthFixture fixture = await PrepareCallbackAsync();
        fixture.Provider.Identity = claim switch
        {
            "subject" => new OAuthProviderIdentity(
                new string('s', 257),
                "person@example.com",
                EmailVerified: true,
                "Person"),
            "email" => new OAuthProviderIdentity(
                "subject",
                $"{new string('e', 310)}@example.com",
                EmailVerified: true,
                "Person"),
            _ => new OAuthProviderIdentity(
                "subject",
                "person@example.com",
                EmailVerified: true,
                new string('n', 201))
        };

        ServiceResult<Uri> result = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);

        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal(0, fixture.Store.ResolveCalls);
    }

    // Verifies that external provider payload failures are reduced to sanitized OAuth errors.
    [Fact]
    public async Task OAuthCallbackSanitizesExternalProviderPayloadFailures()
    {
        OAuthFixture fixture = await PrepareCallbackAsync();
        fixture.Provider.Failure = new ExternalOAuthProviderException();

        ServiceResult<Uri> result = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);

        Assert.Equal(AuthError.ExternalProviderFailure, result.Error);
        Assert.Equal("oauth_provider_failed", result.Code);
        Assert.Equal("oauth_login_failed", fixture.Audit.LastEventType);
        Assert.Equal(0, fixture.Store.ResolveCalls);
    }

    [Fact]
    public async Task OAuthCallbackCoversAccountAndExchangeFailures()
    {
        OAuthFixture conflict = await PrepareCallbackAsync();
        conflict.Provider.Identity = ValidIdentity();
        conflict.Store.ResolveResult = ServiceResult<AuthUser>.Failure(
            AuthError.Conflict,
            "unsafe_account_link");
        ServiceResult<Uri> conflictResult = await conflict.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.Conflict, conflictResult.Error);

        OAuthFixture exchangeFailure = await PrepareCallbackAsync();
        exchangeFailure.Provider.Identity = ValidIdentity();
        exchangeFailure.Store.ResolveResult = ServiceResult<AuthUser>.Success(ActiveUser());
        exchangeFailure.Store.CreateTokenResult = false;
        ServiceResult<Uri> exchangeFailureResult = await exchangeFailure.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);
        Assert.Equal(AuthError.ExternalProviderFailure, exchangeFailureResult.Error);
    }

    [Fact]
    public async Task OAuthCallbackReturnsExchangeCodeInFragment()
    {
        OAuthFixture fixture = await PrepareCallbackAsync();
        fixture.Provider.Identity = ValidIdentity();
        fixture.Store.ResolveResult = ServiceResult<AuthUser>.Success(ActiveUser());
        fixture.Store.CreateTokenResult = true;

        ServiceResult<Uri> result = await fixture.Service.HandleCallbackAsync(
            "google",
            "code",
            "state-value",
            null,
            Metadata);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value.Query);
        Assert.Equal("#oauth_code=exchange-code", result.Value.Fragment);
        Assert.Equal("oauth_login_success", fixture.Audit.LastEventType);
        Assert.Equal("oauth_exchange:google", fixture.Store.CreatedTokenPurpose);
        Assert.Equal("hash:exchange-code", fixture.Store.CreatedTokenHash);
        Assert.Equal("oauth_account_linked", fixture.Store.ResolveAudit!.EventType);
        Assert.Equal(Now, fixture.Store.ResolveAudit.CreatedUtc);
        Assert.Equal(Metadata.IpAddress, fixture.Store.ResolveAudit.IpAddress);
        Assert.Equal(Metadata.UserAgent, fixture.Store.ResolveAudit.UserAgent);
        Assert.Equal("provider=google", fixture.Store.ResolveAudit.Detail);
    }

    [Fact]
    public async Task OAuthExchangeValidatesCodeTokenAndUser()
    {
        OAuthFixture fixture = CreateOAuthFixture();

        Assert.Equal(
            AuthError.Unauthorized,
            (await fixture.Service.ExchangeAsync("unknown", "code", null, Metadata)).Error);
        Assert.Equal(
            AuthError.Unauthorized,
            (await fixture.Service.ExchangeAsync("google", null, null, Metadata)).Error);
        Assert.Equal(
            AuthError.Unauthorized,
            (await fixture.Service.ExchangeAsync(
                "google",
                new string('x', 1_025),
                null,
                Metadata)).Error);

        ServiceResult<SessionTokens> missingToken = await fixture.Service.ExchangeAsync(
            "google",
            "exchange-code",
            null,
            Metadata);
        Assert.Equal(AuthError.Unauthorized, missingToken.Error);
        Assert.Equal(2, fixture.Store.ConsumedTokenHashes.Count);

        OAuthFixture missingUser = PreparedExchangeFixture();
        missingUser.Store.User = null;
        Assert.Equal(
            AuthError.Unauthorized,
            (await missingUser.Service.ExchangeAsync(
                "google",
                "exchange-code",
                null,
                Metadata)).Error);

        OAuthFixture inactiveUser = PreparedExchangeFixture();
        inactiveUser.Store.User = ActiveUser() with { IsActive = false };
        Assert.Equal(
            AuthError.Unauthorized,
            (await inactiveUser.Service.ExchangeAsync(
                "google",
                "exchange-code",
                null,
                Metadata)).Error);

        OAuthFixture unverifiedUser = PreparedExchangeFixture();
        unverifiedUser.Store.User = ActiveUser() with { EmailVerifiedUtc = null };
        Assert.Equal(
            AuthError.Unauthorized,
            (await unverifiedUser.Service.ExchangeAsync(
                "google",
                "exchange-code",
                null,
                Metadata)).Error);
    }

    [Fact]
    public async Task OAuthExchangeEnforcesTenantMembershipAndIssuesSession()
    {
        OAuthFixture tenancyDisabled = PreparedExchangeFixture(
            options => options.Features.Tenancy = false);
        ServiceResult<SessionTokens> disabledResult = await tenancyDisabled.Service.ExchangeAsync(
            "google",
            "exchange-code",
            TenantId,
            Metadata);
        Assert.Equal(AuthError.Forbidden, disabledResult.Error);

        OAuthFixture notMember = PreparedExchangeFixture();
        notMember.Store.TenantMember = false;
        ServiceResult<SessionTokens> notMemberResult = await notMember.Service.ExchangeAsync(
            "google",
            "exchange-code",
            TenantId,
            Metadata);
        Assert.Equal(AuthError.Forbidden, notMemberResult.Error);

        OAuthFixture success = PreparedExchangeFixture();
        success.Store.TenantMember = true;
        success.Session.Result = ServiceResult<SessionTokens>.Success(new SessionTokens(
            "access",
            Now.AddMinutes(10),
            "refresh",
            Now.AddDays(1)));
        ServiceResult<SessionTokens> successResult = await success.Service.ExchangeAsync(
            "google",
            "exchange-code",
            TenantId,
            Metadata);

        Assert.True(successResult.Succeeded);
        Assert.Equal(TenantId, success.Session.TenantId);
        Assert.Equal(UserId, success.Session.User?.Id);
    }

    public static TheoryData<Action<AuthOptions>> NullSecurityNestedOptions => new()
    {
        options => options.Migrations = null!,
        options => options.Passwords.BreachedPasswords = null!,
        options => options.AccessTokenSigning = null!,
        options => options.SecurityLimits = null!
    };

    public static TheoryData<Action<AuthOptions>, string> InvalidSigningOptions => new()
    {
        {
            options => options.AccessTokenSigning.HmacSha256Keys = null!,
            "HmacSha256Keys cannot be null"
        },
        {
            options => options.AccessTokenSigning.UseHostKeyRing = true,
            "cannot be combined"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "missing";
                options.AccessTokenSigning.HmacSha256Keys["current"] = SigningKey();
            },
            "contain ActiveKeyId"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "key-0";
                for (int index = 0; index < 17; index++)
                {
                    options.AccessTokenSigning.HmacSha256Keys[$"key-{index}"] = SigningKey();
                }
            },
            "more than 16"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "null-key";
                options.AccessTokenSigning.HmacSha256Keys["null-key"] = null!;
            },
            "cannot be null"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "current";
                options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
                {
                    Key = StrongSecret,
                    NotBeforeUtc = Now,
                    RetiredUtc = Now.AddMinutes(-1)
                };
            },
            "invalid validity window"
        },
        {
            options =>
            {
                options.JwtSigningKey = string.Empty;
                options.AccessTokenSigning.ActiveKeyId = "current";
                options.AccessTokenSigning.HmacSha256Keys["current"] = new HmacAccessTokenSigningKeyOptions
                {
                    Key = StrongSecret,
                    ActivatedUtc = Now.AddDays(-1),
                    RetiredUtc = TestOptions.Now.AddMinutes(-1)
                };
            },
            "cannot identify a retired key"
        }
    };

    public static TheoryData<Action<AuthOptions>, string> InvalidTokenHashingOptions => new()
    {
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.Keys = null!;
            },
            "Keys cannot be null"
        },
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.CurrentKeyVersion = "missing";
                options.TokenHashing.Keys["v1"] = StrongSecret;
            },
            "contain CurrentKeyVersion"
        },
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.Keys["v1"] = StrongSecret;
                options.TokenHashing.LegacyUnversionedKeyVersion = "legacy";
            },
            "contain LegacyUnversionedKeyVersion"
        },
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.CurrentKeyVersion = "key-0";
                options.TokenHashing.LegacyUnversionedKeyVersion = null;
                for (int index = 0; index < 17; index++)
                {
                    options.TokenHashing.Keys[$"key-{index}"] = StrongSecret;
                }
            },
            "more than 16"
        },
        {
            options =>
            {
                options.TokenHashing.Key = string.Empty;
                options.TokenHashing.CurrentKeyVersion = "bad version";
                options.TokenHashing.LegacyUnversionedKeyVersion = null;
                options.TokenHashing.Keys["bad version"] = StrongSecret;
            },
            "without whitespace"
        }
    };

    private static HmacAccessTokenSigningKeyOptions SigningKey() => new()
    {
        Key = StrongSecret,
        ActivatedUtc = Now.AddDays(-1)
    };

    private static OAuthProviderIdentity ValidIdentity() => new(
        "provider-subject",
        "person@example.com",
        EmailVerified: true,
        "Person");

    private static AuthUser ActiveUser() => new(
        UserId,
        "person@example.com",
        "PERSON@EXAMPLE.COM",
        null,
        Now,
        IsActive: true,
        FailedLoginAttempts: 0,
        LockoutEndUtc: null,
        SecurityVersion: 1,
        CreatedUtc: Now,
        UpdatedUtc: Now);

    private static OAuthFixture CreateOAuthFixture(Action<AuthOptions>? configure = null)
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        google.AuthorizationEndpoint = new Uri("https://oauth.example/authorize");
        google.TokenEndpoint = new Uri("https://oauth.example/token");
        google.JsonWebKeySetEndpoint = new Uri("https://oauth.example/jwks");
        google.ValidIssuers = ["https://oauth.example"];
        google.AllowedHosts = ["oauth.example"];
        configure?.Invoke(options);

        IAuthOAuthPersistenceStore store = OAuthStoreProxy.Create(out OAuthStoreProxy proxy);
        FakeExternalProvider provider = new();
        FakeTokenProtector tokens = new();
        FakeClock clock = new();
        FakeAuditService audit = new();
        FakeSessionIssuer session = new();
        OAuthService service = new(
            [provider],
            store,
            tokens,
            new InputValidator(Options.Create(options)),
            new EphemeralDataProtectionProvider(),
            clock,
            audit,
            session,
            Options.Create(options));
        return new OAuthFixture(service, provider, proxy, audit, session);
    }

    private static async Task<OAuthFixture> PrepareCallbackAsync()
    {
        OAuthFixture fixture = CreateOAuthFixture();
        ServiceResult<Uri> challenge = await fixture.Service.CreateChallengeAsync(
            "google",
            "/return",
            Metadata);
        Assert.True(challenge.Succeeded);
        Assert.NotNull(fixture.Store.SavedState);
        fixture.Store.StateToConsume = fixture.Store.SavedState;
        fixture.Store.ExpectedStateHash = "hash:state-value";
        return fixture;
    }

    private static OAuthFixture PreparedExchangeFixture(Action<AuthOptions>? configure = null)
    {
        OAuthFixture fixture = CreateOAuthFixture(configure);
        fixture.Store.ExpectedTokenHash = "hash:exchange-code";
        fixture.Store.TokenToConsume = new OneTimeTokenRecord(
            UserId,
            "oauth_exchange:google",
            Now.AddMinutes(5));
        fixture.Store.User = ActiveUser();
        return fixture;
    }

    private sealed record OAuthFixture(
        OAuthService Service,
        FakeExternalProvider Provider,
        OAuthStoreProxy Store,
        FakeAuditService Audit,
        FakeSessionIssuer Session);

    private sealed class FakeExternalProvider : IExternalOAuthProvider
    {
        public bool IsEnabled { get; set; } = true;

        bool IExternalOAuthProvider.IsEnabled(string provider) =>
            IsEnabled && string.Equals(provider, "google", StringComparison.Ordinal);

        public OAuthProviderIdentity? Identity { get; set; }

        public ExternalOAuthProviderException? Failure { get; set; }

        public string? State { get; private set; }

        public string? CodeChallenge { get; private set; }

        public string? Nonce { get; private set; }

        // Captures challenge inputs and returns the fake provider authorization endpoint.
        public Uri CreateAuthorizationUri(
            string provider,
            string state,
            string codeChallenge,
            string nonce)
        {
            Assert.Equal("google", provider);
            State = state;
            CodeChallenge = codeChallenge;
            Nonce = nonce;
            return new Uri($"https://oauth.example/authorize?state={Uri.EscapeDataString(state)}");
        }

        public Task<OAuthProviderIdentity?> ExchangeAndValidateAsync(
            string provider,
            string code,
            string codeVerifier,
            string expectedNonce,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("google", provider);
            if (Failure is not null)
            {
                throw Failure;
            }

            _ = code;
            _ = codeVerifier;
            _ = expectedNonce;
            return Task.FromResult(Identity);
        }
    }

    private sealed class FakeTokenProtector : ITokenProtector
    {
        private static readonly string[] GeneratedValues =
        [
            "state-value",
            "verifier-value",
            "nonce-value",
            "exchange-code"
        ];

        private readonly Queue<string> _generated = new(GeneratedValues);

        public string Generate(int byteLength = 48)
        {
            _ = byteLength;
            return _generated.Dequeue();
        }

        public string Hash(string rawToken) => "hash:" + rawToken;

        public IReadOnlyList<string> HashCandidates(string rawToken) =>
            ["legacy:" + rawToken, Hash(rawToken)];
    }

    private sealed class FakeClock : IAuthClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeAuditService : IAuditService
    {
        public string? LastEventType { get; private set; }

        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEventType = eventType;
            _ = userId;
            _ = tenantId;
            _ = ipAddress;
            _ = userAgent;
            _ = detail;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionIssuer : IAuthSessionIssuer
    {
        public ServiceResult<SessionTokens> Result { get; set; } =
            ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "not_configured");

        public AuthUser? User { get; private set; }

        public Guid? TenantId { get; private set; }

        public Task<ServiceResult<SessionTokens>> IssueSessionAsync(
            AuthUser user,
            Guid? tenantId,
            Guid? familyId,
            RequestMetadata metadata,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            User = user;
            TenantId = tenantId;
            _ = familyId;
            _ = metadata;
            return Task.FromResult(Result);
        }

        public Task<UserContext> BuildContextAsync(
            AuthUser user,
            Guid? tenantId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public AccessTokenResult CreateAccessToken(UserContext context) =>
            throw new NotSupportedException();

        public (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
            AuthUser user,
            Guid familyId,
            RequestMetadata metadata,
            DateTimeOffset now) =>
            throw new NotSupportedException();
    }

    [SuppressMessage(
        "Performance",
        "CA1852:Seal internal types",
        Justification = "DispatchProxy requires a non-sealed proxy base type for runtime subclass generation.")]
    private class OAuthStoreProxy : DispatchProxy
    {
        public OAuthStateRecord? SavedState { get; set; }

        public OAuthStateRecord? StateToConsume { get; set; }

        public string? ExpectedStateHash { get; set; }

        public List<string> ConsumedStateHashes { get; } = [];

        public ServiceResult<AuthUser> ResolveResult { get; set; } =
            ServiceResult<AuthUser>.Failure(AuthError.NotFound, "not_configured");

        public int ResolveCalls { get; private set; }

        public AuditRecord? ResolveAudit { get; private set; }

        public bool CreateTokenResult { get; set; }

        public string? CreatedTokenPurpose { get; private set; }

        public string? CreatedTokenHash { get; private set; }

        public OneTimeTokenRecord? TokenToConsume { get; set; }

        public string? ExpectedTokenHash { get; set; }

        public List<string> ConsumedTokenHashes { get; } = [];

        public AuthUser? User { get; set; }

        public bool TenantMember { get; set; }

        public static IAuthOAuthPersistenceStore Create(out OAuthStoreProxy proxy)
        {
            IAuthOAuthPersistenceStore store =
                DispatchProxy.Create<IAuthOAuthPersistenceStore, OAuthStoreProxy>();
            proxy = (OAuthStoreProxy)(object)store;
            return store;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IAuthOAuthStore.SaveOAuthStateAsync) => SaveState(args!),
                nameof(IAuthOAuthStore.ConsumeOAuthStateAsync) => ConsumeState(args!),
                nameof(IAuthOAuthStore.ResolveOAuthUserAsync) => ResolveOAuthUser(args!),
                nameof(IAuthOneTimeTokenStore.CreateOneTimeTokenAsync) => CreateToken(args!),
                nameof(IAuthOneTimeTokenStore.ConsumeOneTimeTokenAsync) => ConsumeToken(args!),
                nameof(IAuthUserStore.FindUserByIdAsync) => Task.FromResult(User),
                nameof(IAuthTenantStore.IsTenantMemberAsync) => Task.FromResult(TenantMember),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };

        // Records and returns the configured OAuth-user resolution result.
        private Task<ServiceResult<AuthUser>> ResolveOAuthUser(object?[] args)
        {
            ResolveCalls++;
            ResolveAudit = (AuditRecord)args[5]!;
            return Task.FromResult(ResolveResult);
        }

        private Task SaveState(object?[] args)
        {
            SavedState = (OAuthStateRecord)args[0]!;
            return Task.CompletedTask;
        }

        private Task<OAuthStateRecord?> ConsumeState(object?[] args)
        {
            string hash = (string)args[1]!;
            ConsumedStateHashes.Add(hash);
            return Task.FromResult(
                string.Equals(hash, ExpectedStateHash, StringComparison.Ordinal)
                    ? StateToConsume
                    : null);
        }

        private Task<bool> CreateToken(object?[] args)
        {
            CreatedTokenPurpose = (string)args[1]!;
            CreatedTokenHash = (string)args[2]!;
            return Task.FromResult(CreateTokenResult);
        }

        private Task<OneTimeTokenRecord?> ConsumeToken(object?[] args)
        {
            string hash = (string)args[1]!;
            ConsumedTokenHashes.Add(hash);
            return Task.FromResult(
                string.Equals(hash, ExpectedTokenHash, StringComparison.Ordinal)
                    ? TokenToConsume
                    : null);
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "SharpAccess.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
