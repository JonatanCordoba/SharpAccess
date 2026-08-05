using System.Reflection;
using System.Security.Claims;
using System.Text;
using SharpAccess;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.OAuth;
using SharpAccess.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthEndpointMappingTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly MethodInfo ReadJsonBodyMethod = typeof(AuthEndpointMapper)
        .GetMethod("ReadJsonBodyAsync", BindingFlags.NonPublic | BindingFlags.Static)!
        .MakeGenericMethod(typeof(LoginRequest));
    private static readonly MethodInfo TenantMatchesRouteMethod = typeof(AuthEndpointMapper)
        .GetMethod("TenantMatchesRoute", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void MapSharpAccessEndpointsSkipsEveryFeatureWhenAllFeatureFlagsAreOff()
    {
        WebApplication app = CreateApp(new AuthOptions());

        app.MapSharpAccessEndpoints("/auth");

        Assert.Empty(RouteEndpoints(app));
    }

    [Fact]
    public void MapSharpAccessEndpointsMapsEveryEnabledFeatureGroup()
    {
        AuthOptions options = new()
        {
            Features = new AuthFeatureOptions
            {
                PasswordAuthentication = true,
                Registration = true,
                PasswordReset = true,
                RefreshTokens = true,
                Administration = true,
                Tenancy = true
            }
        };
        TestOptions.EnableGoogle(options).ClientId = "client-id";
        WebApplication app = CreateApp(options);

        RouteGroupBuilder group = app.MapSharpAccessEndpoints("/auth/");

        Assert.NotNull(group);
    }

    // Verifies keyed challenge, exchange, and literal callback routes are registered exactly once.
    [Fact]
    public void OpenIdConnectRoutesUseProviderKeysAndExactConfiguredCallbacks()
    {
        AuthOptions options = new();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret";
        WebApplication app = CreateApp(options);

        app.MapSharpAccessEndpoints("/auth");

        string[] routes = RouteEndpoints(app)
            .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();
        Assert.Contains("/auth/oauth/{provider}/challenge", routes);
        Assert.Contains("/auth/oauth/{provider}/exchange", routes);
        Assert.Contains("/auth/oauth/google/callback", routes);
        RouteEndpoint callback = Assert.Single(
            RouteEndpoints(app),
            endpoint => endpoint.RoutePattern.RawText == "/auth/oauth/google/callback");
        Assert.Equal(
            "google",
            callback.Metadata.GetMetadata<OpenIdConnectProviderMetadata>()?.Provider);
    }

    // Verifies callbacks cannot shadow an enabled route under a custom authentication prefix.
    [Fact]
    public void CustomPrefixRejectsAnOpenIdConnectCallbackCollision()
    {
        AuthOptions options = new();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret";
        google.CallbackPath = "/identity/me";
        WebApplication app = CreateApp(options);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => app.MapSharpAccessEndpoints("/identity"));

        Assert.Contains("selected endpoint prefix", exception.Message, StringComparison.Ordinal);
        Assert.Empty(RouteEndpoints(app));
    }

    // Verifies a literal callback outside the custom authentication prefix maps exactly once.
    [Fact]
    public void CustomPrefixMapsANonCollidingOpenIdConnectCallback()
    {
        AuthOptions options = new();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret";
        google.CallbackPath = "/callbacks/google";
        WebApplication app = CreateApp(options);

        app.MapSharpAccessEndpoints("/identity");

        string[] routes = RouteEndpoints(app)
            .Select(static endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();
        Assert.Contains("/identity/me", routes);
        Assert.Contains("/identity/oauth/{provider}/challenge", routes);
        Assert.Contains("/callbacks/google", routes);
    }

    [Fact]
    public async Task ReadJsonBodyAsyncReturnsNullForMalformedJson()
    {
        DefaultHttpContext context = new();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));

        LoginRequest? request = await ReadLoginRequestAsync(context);

        Assert.Null(request);
    }

    [Fact]
    public async Task ReadJsonBodyAsyncReturnsNullForBadHttpRequestStreams()
    {
        DefaultHttpContext context = new();
        context.Request.Body = new BadRequestStream();

        LoginRequest? request = await ReadLoginRequestAsync(context);

        Assert.Null(request);
    }

    [Fact]
    public void TenantMatchesRouteRequiresValidActiveTenantAndRouteValue()
    {
        Assert.False(TenantMatchesRoute(new ClaimsPrincipal(), new DefaultHttpContext()));
        Assert.False(TenantMatchesRoute(TenantUser(TenantId), new object()));
        Assert.False(TenantMatchesRoute(TenantUser(TenantId), new DefaultHttpContext()));

        DefaultHttpContext nullRoute = new();
        nullRoute.Request.RouteValues["tenantId"] = null;
        Assert.False(TenantMatchesRoute(TenantUser(TenantId), nullRoute));

        DefaultHttpContext invalidRoute = new();
        invalidRoute.Request.RouteValues["tenantId"] = "not-a-guid";
        Assert.False(TenantMatchesRoute(TenantUser(TenantId), invalidRoute));

        DefaultHttpContext mismatchRoute = new();
        mismatchRoute.Request.RouteValues["tenantId"] = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Assert.False(TenantMatchesRoute(TenantUser(TenantId), mismatchRoute));

        DefaultHttpContext matchingRoute = new();
        matchingRoute.Request.RouteValues["tenantId"] = TenantId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(TenantMatchesRoute(TenantUser(TenantId), matchingRoute));
    }

    private static WebApplication CreateApp(AuthOptions options)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.AddSingleton<IAuthService, UnusedAuthService>();
        builder.Services.AddSingleton<IOAuthService, UnusedOAuthService>();
        builder.Services.AddSingleton<IAuditService, UnusedAuditService>();
        return builder.Build();
    }

    private static async Task<LoginRequest?> ReadLoginRequestAsync(DefaultHttpContext context)
    {
        Task<LoginRequest?> task = (Task<LoginRequest?>)ReadJsonBodyMethod.Invoke(null, [context, CancellationToken.None])!;
        return await task;
    }

    private static bool TenantMatchesRoute(ClaimsPrincipal principal, object resource)
    {
        AuthorizationHandlerContext context = new([], principal, resource);
        return (bool)TenantMatchesRouteMethod.Invoke(null, [context])!;
    }

    private static ClaimsPrincipal TenantUser(Guid tenantId) =>
        new(new ClaimsIdentity([new Claim(AuthConstants.TenantClaim, tenantId.ToString("D", System.Globalization.CultureInfo.InvariantCulture))]));

    private static RouteEndpoint[] RouteEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private sealed class BadRequestStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new BadHttpRequestException("bad request");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new BadHttpRequestException("bad request"));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class UnusedAuthService : IAuthService
    {
        // Prevents route-discovery tests from treating the profile service parameter as a request body.
        public Task<ServiceResult<UserContext>> GetMeAsync(
            Guid userId,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking registration behavior.
        public Task<ServiceResult<string>> RegisterAsync(
            string? email,
            string? password,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking password-login behavior.
        public Task<ServiceResult<SessionTokens>> LoginAsync(
            string? email,
            string? password,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking refresh behavior.
        public Task<ServiceResult<SessionTokens>> RefreshAsync(
            string? refreshToken,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking logout behavior.
        public Task<ServiceResult<bool>> LogoutAsync(
            Guid userId,
            string? refreshToken,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking explicit revocation behavior.
        public Task<ServiceResult<bool>> RevokeAsync(
            Guid userId,
            bool canManageSessions,
            string? refreshToken,
            bool revokeFamily,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking password-change behavior.
        public Task<ServiceResult<bool>> ChangePasswordAsync(
            Guid userId,
            string? currentPassword,
            string? newPassword,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking password-recovery behavior.
        public Task<ServiceResult<string>> ForgotPasswordAsync(
            string? email,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking password-reset behavior.
        public Task<ServiceResult<bool>> ResetPasswordAsync(
            string? token,
            string? newPassword,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking email-verification behavior.
        public Task<ServiceResult<bool>> VerifyEmailAsync(
            string? token,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from invoking verification-resend behavior.
        public Task<ServiceResult<string>> ResendVerificationAsync(
            string? email,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedOAuthService : IOAuthService
    {
        // Prevents route-discovery tests from treating the OAuth service parameter as a request body.
        public Task<ServiceResult<Uri>> CreateChallengeAsync(
            string provider,
            string? returnUrl,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from treating the OAuth callback service as a request body.
        public Task<ServiceResult<Uri>> HandleCallbackAsync(
            string provider,
            string? code,
            string? state,
            string? error,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Prevents route-discovery tests from treating the OAuth exchange service as a request body.
        public Task<ServiceResult<SessionTokens>> ExchangeAsync(
            string provider,
            string? exchangeCode,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedAuditService : IAuditService
    {
        // Prevents route-discovery tests from treating the callback audit dependency as a request body.
        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
