using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using SharpAccess;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class CoreServiceRegistrationAndJwtValidationTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Base64SigningKey = Enumerable.Range(1, 32)
        .Select(static value => (byte)value)
        .ToArray();

    [Fact]
    public void AddSharpAccessValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => AuthServiceCollectionExtensions.AddSharpAccess(null!, static _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSharpAccess((Action<AuthOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => AuthServiceCollectionExtensions.AddSharpAccess(null!, new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSharpAccess((IConfiguration)null!));
    }

    [Fact]
    public void AddSharpAccessConfigurationOverloadBindsThenAppliesCodeOverrides()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpAccess:BaseUri"] = "https://api.example.com",
                ["SharpAccess:JwtIssuer"] = "configured-issuer",
                ["SharpAccess:JwtAudience"] = "configured-audience",
                ["SharpAccess:JwtSigningKey"] = "CONFIGURED-JWT-SIGNING-KEY-12345678901234567890",
                ["SharpAccess:Features:PasswordAuthentication"] = "true",
                ["SharpAccess:Passwords:Peppers:v1"] = "configured-pepper-value",
                ["SharpAccess:Passwords:CurrentPepperVersion"] = "v1",
                ["SharpAccess:TokenHashing:Key"] = "CONFIGURED-TOKEN-HASHING-KEY-12345678901234567890",
                ["SharpAccess:RateLimits:PartitionKey"] = "CONFIGURED-RATE-LIMIT-KEY-12345678901234567890",
                ["SharpAccess:RateLimits:LoginPerMinute"] = "42"
            })
            .Build();
        ServiceCollection services = new();

        services.AddSharpAccess(configuration, options =>
        {
            OpenIdConnectProviderOptions google = options.OpenIdConnect.Providers["google"];
            google.Enabled = true;
            google.ClientId = "client-id";
            google.ClientSecret = "client-secret-value";
            options.RateLimits.OAuthPerMinute = 9;
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        AuthOptions options = provider.GetRequiredService<IOptions<AuthOptions>>().Value;
        Assert.Equal(new Uri("https://api.example.com"), options.BaseUri);
        Assert.Equal("configured-issuer", options.JwtIssuer);
        Assert.Equal("configured-audience", options.JwtAudience);
        Assert.True(options.Features.PasswordAuthentication);
        Assert.True(options.OpenIdConnect.Providers["google"].Enabled);
        Assert.Equal(42, options.RateLimits.LoginPerMinute);
        Assert.Equal(9, options.RateLimits.OAuthPerMinute);
        Assert.Equal("client-secret-value", options.OpenIdConnect.Providers["google"].ClientSecret);
    }

    [Fact]
    public void AddSharpAccessConfiguresJwtBearerOptionsWithPlainTextSigningKey()
    {
        AuthOptions authOptions = TestOptions.Create();
        authOptions.BaseUri = new Uri("http://localhost");
        ServiceCollection services = new();

        services.AddSharpAccess(options => CopyOptions(authOptions, options));
        using ServiceProvider provider = services.BuildServiceProvider();

        JwtBearerOptions bearer = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(AuthConstants.AuthenticationScheme);
        IAccessTokenSigningKeyRing ring = provider.GetRequiredService<IAccessTokenSigningKeyRing>();
        SymmetricSecurityKey signingKey = Assert.IsType<SymmetricSecurityKey>(ring.ActiveSigningKey.VerificationKey);

        Assert.False(bearer.MapInboundClaims);
        Assert.False(bearer.RequireHttpsMetadata);
        Assert.False(bearer.SaveToken);
        Assert.Equal("test-issuer", bearer.TokenValidationParameters.ValidIssuer);
        Assert.Equal("test-audience", bearer.TokenValidationParameters.ValidAudience);
        Assert.Equal(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey), signingKey.Key);
        Assert.NotNull(bearer.TokenValidationParameters.IssuerSigningKeyResolver);
        Assert.NotNull(bearer.Events.OnTokenValidated);
        Assert.NotNull(bearer.Events.OnChallenge);
        Assert.NotNull(bearer.Events.OnForbidden);
    }

    [Fact]
    public void AddSharpAccessConfiguresJwtBearerOptionsWithBase64SigningKey()
    {
        AuthOptions authOptions = TestOptions.Create();
        authOptions.JwtSigningKey = Convert.ToBase64String(Base64SigningKey);
        ServiceCollection services = new();

        services.AddSharpAccess(options => CopyOptions(authOptions, options));
        using ServiceProvider provider = services.BuildServiceProvider();

        JwtBearerOptions bearer = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(AuthConstants.AuthenticationScheme);
        IAccessTokenSigningKeyRing ring = provider.GetRequiredService<IAccessTokenSigningKeyRing>();
        SymmetricSecurityKey signingKey = Assert.IsType<SymmetricSecurityKey>(ring.ActiveSigningKey.VerificationKey);

        Assert.True(bearer.RequireHttpsMetadata);
        Assert.Equal(Base64SigningKey, signingKey.Key);
        Assert.Equal(TimeSpan.FromSeconds(30), bearer.TokenValidationParameters.ClockSkew);
        Assert.Equal("sub", bearer.TokenValidationParameters.NameClaimType);
        Assert.Equal(AuthConstants.GlobalRoleClaim, bearer.TokenValidationParameters.RoleClaimType);
        Assert.Contains(SecurityAlgorithms.HmacSha256, bearer.TokenValidationParameters.ValidAlgorithms);
    }

    [Fact]
    public void JwtBearerConfigurationRejectsBlankSigningMaterial()
    {
        AuthOptions authOptions = TestOptions.Create();
        authOptions.JwtSigningKey = " ";
        JwtBearerOptions bearer = new();

        Assert.Throws<InvalidOperationException>(() => ConfigureJwtBearer(bearer, authOptions));
    }

    [Fact]
    public async Task TokenValidationFailsWhenRequiredIdentityClaimsAreInvalid()
    {
        JwtBearerOptions bearer = ConfiguredBearerOptions();
        TokenValidatedContext context = TokenContext(bearer, Principal([new Claim("sub", "not-a-guid")]));

        await bearer.Events.OnTokenValidated!(context);

        Assert.NotNull(context.Result?.Failure);
    }

    [Trait("MutationInvariant", "AccountState")]
    [Fact]
    public async Task TokenValidationRejectsMissingInactiveUnverifiedOrVersionChangedUsers()
    {
        IAuthStore store = StoreProxy.Create(out StoreProxy proxy);
        JwtBearerOptions bearer = ConfiguredBearerOptions();

        TokenValidatedContext missingUser = TokenContext(bearer, ValidPrincipal(), store);
        await bearer.Events.OnTokenValidated!(missingUser);
        Assert.NotNull(missingUser.Result?.Failure);

        proxy.User = User(isActive: false, verified: true, securityVersion: 7);
        TokenValidatedContext inactiveUser = TokenContext(bearer, ValidPrincipal(), store);
        await bearer.Events.OnTokenValidated!(inactiveUser);
        Assert.NotNull(inactiveUser.Result?.Failure);

        proxy.User = User(isActive: true, verified: false, securityVersion: 7);
        TokenValidatedContext unverifiedUser = TokenContext(bearer, ValidPrincipal(), store);
        await bearer.Events.OnTokenValidated!(unverifiedUser);
        Assert.NotNull(unverifiedUser.Result?.Failure);

        proxy.User = User(isActive: true, verified: true, securityVersion: 8);
        TokenValidatedContext changedVersion = TokenContext(bearer, ValidPrincipal(), store);
        await bearer.Events.OnTokenValidated!(changedVersion);
        Assert.NotNull(changedVersion.Result?.Failure);
    }

    [Trait("MutationInvariant", "TenantIsolation")]
    [Fact]
    public async Task TokenValidationChecksTenantMembershipWhenTenantClaimIsPresent()
    {
        IAuthStore store = StoreProxy.Create(out StoreProxy proxy);
        proxy.User = User(isActive: true, verified: true, securityVersion: 7);
        proxy.TenantMember = false;
        JwtBearerOptions bearer = ConfiguredBearerOptions();

        TokenValidatedContext invalidTenant = TokenContext(bearer, ValidPrincipal(new Claim(AuthConstants.TenantClaim, "not-a-guid")), store);
        await bearer.Events.OnTokenValidated!(invalidTenant);
        Assert.NotNull(invalidTenant.Result?.Failure);

        TokenValidatedContext missingMembership = TokenContext(bearer, ValidPrincipal(new Claim(AuthConstants.TenantClaim, TenantId.ToString("D", CultureInfo.InvariantCulture))), store);
        await bearer.Events.OnTokenValidated!(missingMembership);
        Assert.NotNull(missingMembership.Result?.Failure);

        proxy.TenantMember = true;
        TokenValidatedContext validTenant = TokenContext(bearer, ValidPrincipal(new Claim(AuthConstants.TenantClaim, TenantId.ToString("D", CultureInfo.InvariantCulture))), store);
        await bearer.Events.OnTokenValidated!(validTenant);
        Assert.Null(validTenant.Result?.Failure);
    }

    [Fact]
    public async Task JwtBearerChallengeAndForbiddenWriteProblemDetailsAndRespectStartedResponses()
    {
        JwtBearerOptions bearer = ConfiguredBearerOptions();
        AuthenticationScheme scheme = Scheme();

        DefaultHttpContext challengeContextHttp = HttpContextWithServices();
        JwtBearerChallengeContext challenge = new(challengeContextHttp, scheme, bearer, new AuthenticationProperties());
        await bearer.Events.OnChallenge!(challenge);
        Assert.Equal(StatusCodes.Status401Unauthorized, challengeContextHttp.Response.StatusCode);
        Assert.Equal("application/problem+json", challengeContextHttp.Response.ContentType);

        DefaultHttpContext startedChallengeHttp = StartedHttpContext();
        JwtBearerChallengeContext startedChallenge = new(startedChallengeHttp, scheme, bearer, new AuthenticationProperties());
        await bearer.Events.OnChallenge!(startedChallenge);
        Assert.True(startedChallengeHttp.Response.HasStarted);

        DefaultHttpContext forbiddenContextHttp = HttpContextWithServices();
        ForbiddenContext forbidden = new(forbiddenContextHttp, scheme, bearer);
        await bearer.Events.OnForbidden!(forbidden);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenContextHttp.Response.StatusCode);
        Assert.Equal("application/problem+json", forbiddenContextHttp.Response.ContentType);

        DefaultHttpContext startedForbiddenHttp = StartedHttpContext();
        ForbiddenContext startedForbidden = new(startedForbiddenHttp, scheme, bearer);
        await bearer.Events.OnForbidden!(startedForbidden);
        Assert.True(startedForbiddenHttp.Response.HasStarted);
    }

    private static JwtBearerOptions ConfiguredBearerOptions()
    {
        JwtBearerOptions bearer = new();
        ConfigureJwtBearer(bearer, TestOptions.Create());
        return bearer;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions bearer, AuthOptions options)
    {
        Microsoft.Extensions.DependencyInjection.AuthJwtBearerConfiguration.ConfigureJwtBearer(
            bearer,
            Options.Create(options),
            TestOptions.Clock);
    }

    private static TokenValidatedContext TokenContext(
        JwtBearerOptions bearer,
        ClaimsPrincipal principal,
        IAuthStore? store = null)
    {
        DefaultHttpContext httpContext = HttpContextWithServices(store);
        return new TokenValidatedContext(httpContext, Scheme(), bearer)
        {
            Principal = principal
        };
    }

    private static DefaultHttpContext HttpContextWithServices(IAuthStore? store = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddProblemDetails();
        IAuthStore selectedStore = store ?? StoreProxy.Create(out _);
        services.AddSingleton(selectedStore);
        services.AddSingleton<IAuthUserTenantStore>(selectedStore);
        DefaultHttpContext context = new()
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static DefaultHttpContext StartedHttpContext()
    {
        FeatureCollection features = new();
        features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        return new DefaultHttpContext(features)
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
    }

    private static AuthenticationScheme Scheme() =>
        new(AuthConstants.AuthenticationScheme, AuthConstants.AuthenticationScheme, typeof(JwtBearerHandler));

    private static ClaimsPrincipal ValidPrincipal(params Claim[] additionalClaims)
    {
        List<Claim> claims =
        [
            new Claim("sub", UserId.ToString("D", CultureInfo.InvariantCulture)),
            new Claim(AuthConstants.SecurityVersionClaim, "7"),
            new Claim(AuthConstants.AuthorizationVersionClaim, "7")
        ];
        claims.AddRange(additionalClaims);
        return Principal(claims);
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) => new(new ClaimsIdentity(claims, "unit"));

    private static AuthUser User(bool isActive, bool verified, int securityVersion) => new(
        UserId,
        "person@example.com",
        "PERSON@EXAMPLE.COM",
        null,
        verified ? Now : null,
        isActive,
        FailedLoginAttempts: 0,
        LockoutEndUtc: null,
        securityVersion,
        Now,
        Now);

    private static void CopyOptions(AuthOptions source, AuthOptions target)
    {
        target.BaseUri = source.BaseUri;
        target.JwtIssuer = source.JwtIssuer;
        target.JwtAudience = source.JwtAudience;
        target.JwtSigningKey = source.JwtSigningKey;
        target.AccessTokenMinutes = source.AccessTokenMinutes;
        target.RequireCsrfHeaderForCookieRefreshRequests = source.RequireCsrfHeaderForCookieRefreshRequests;
        target.RefreshTokenCookieName = source.RefreshTokenCookieName;
        target.Features.PasswordAuthentication = source.Features.PasswordAuthentication;
        target.Features.Registration = source.Features.Registration;
        target.Features.PasswordReset = source.Features.PasswordReset;
        target.Features.RefreshTokens = source.Features.RefreshTokens;
        target.Features.Administration = source.Features.Administration;
        target.Features.Tenancy = source.Features.Tenancy;
        target.TokenHashing.Key = source.TokenHashing.Key;
        target.RateLimits.PartitionKey = source.RateLimits.PartitionKey;
        target.Passwords.Iterations = source.Passwords.Iterations;
        target.Passwords.MemorySizeKiB = source.Passwords.MemorySizeKiB;
        target.Passwords.DegreeOfParallelism = source.Passwords.DegreeOfParallelism;
        foreach (KeyValuePair<string, string> pepper in source.Passwords.Peppers)
        {
            target.Passwords.Peppers[pepper.Key] = pepper.Value;
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    [SuppressMessage("Performance", "CA1852:Seal internal types", Justification = "DispatchProxy requires a non-sealed proxy base type for runtime subclass generation.")]
    private class StoreProxy : DispatchProxy
    {
        public AuthUser? User { get; set; }

        public bool TenantMember { get; set; }

        public static IAuthStore Create(out StoreProxy proxy)
        {
            IAuthStore store = DispatchProxy.Create<IAuthStore, StoreProxy>();
            proxy = (StoreProxy)(object)store;
            return store;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                nameof(IAuthStore.FindUserByIdAsync) => Task.FromResult(User),
                nameof(IAuthStore.IsTenantMemberAsync) => Task.FromResult(TenantMember),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
    }
}
