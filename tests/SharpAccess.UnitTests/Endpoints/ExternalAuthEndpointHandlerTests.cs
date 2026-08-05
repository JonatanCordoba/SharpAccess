using System.Net;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.OAuth;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class ExternalAuthEndpointHandlerTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);

    // Verifies that OpenID Connect challenges map service redirects and failures to HTTP results.
    [Fact]
    public async Task OpenIdConnectChallengeMapsRedirectsAndFailures()
    {
        FakeExternalService service = new();
        DefaultHttpContext context = CreateContext();

        Assert.Equal(StatusCodes.Status302Found, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectChallengeAsync("google", "/after", context, service, OpenIdConnectOptions(), CancellationToken.None)));
        Assert.Equal("/after", service.LastReturnUrl);

        service.RedirectResult = ServiceResult<Uri>.Failure(AuthError.ExternalProviderFailure, "external_unavailable");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectChallengeAsync("google", "/after", context, service, OpenIdConnectOptions(), CancellationToken.None)));
    }

    // Verifies that OpenID Connect callbacks map redirects and sanitized failures without duplicate audits.
    [Fact]
    public async Task OpenIdConnectCallbackMapsRedirectsAndFailures()
    {
        FakeExternalService service = new();
        CapturingAuditService audit = new();
        DefaultHttpContext context = CreateContext();
        context.Request.Headers.Cookie =
            "__Secure-sharpaccess_oidc_google=external-state";

        Assert.Equal(StatusCodes.Status302Found, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectCallbackAsync(
                "external-code",
                "external-state",
                null,
                context,
                service,
                audit,
                OpenIdConnectOptions(),
                CancellationToken.None)));
        Assert.Equal("external-code", service.LastCode);
        Assert.Equal("external-state", service.LastState);

        service.RedirectResult = ServiceResult<Uri>.Failure(AuthError.ExternalProviderFailure, "external_callback_failed");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectCallbackAsync(
                null,
                "external-state",
                "denied",
                context,
                service,
                audit,
                OpenIdConnectOptions(),
                CancellationToken.None)));
        Assert.Equal("denied", service.LastError);
        Assert.Equal(0, audit.Calls);
    }

    // Verifies that OpenID Connect exchanges map sessions and authorization failures to HTTP results.
    [Fact]
    public async Task OpenIdConnectExchangeMapsSessionsAndFailures()
    {
        FakeExternalService service = new();
        AuthOptions options = TestOptions.Create();
        DefaultHttpContext context = CreateContext();

        Assert.Equal(StatusCodes.Status200OK, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectExchangeAsync(
                "google",
                new OAuthExchangeRequest("exchange-value", TenantId),
                context,
                service,
                Options.Create(options),
                CancellationToken.None)));
        Assert.Equal("exchange-value", service.LastCode);
        Assert.Equal(TenantId, service.LastTenantId);

        service.SessionResult = ServiceResult<SessionTokens>.Failure(AuthError.Unauthorized, "exchange_rejected");
        Assert.Equal(StatusCodes.Status401Unauthorized, await ExecuteAndGetStatusAsync(
            await AuthEndpointHandlers.OpenIdConnectExchangeAsync(
                "google",
                new OAuthExchangeRequest("exchange-value", TenantId),
                context,
                service,
                Options.Create(options),
                CancellationToken.None)));
    }

    // Creates enabled OpenID Connect options for endpoint mapping tests.
    private static IOptions<AuthOptions> OpenIdConnectOptions()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "test-client-id";
        google.ClientSecret = "test-client-secret";
        return Options.Create(options);
    }

    private static DefaultHttpContext CreateContext()
    {
        DefaultHttpContext context = new()
        {
            RequestServices = Services
        };
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers.UserAgent = "unit-test";
        context.Request.Scheme = "https";
        context.SetEndpoint(new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OpenIdConnectProviderMetadata("google")),
            "google callback"));
        return context;
    }

    private static async Task<int> ExecuteAndGetStatusAsync(IResult result)
    {
        DefaultHttpContext context = CreateContext();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private static SessionTokens Tokens() => new(
        "access-value",
        Now.AddMinutes(5),
        string.Empty,
        Now.AddDays(1));

    private sealed class FakeExternalService : IOAuthService
    {
        public string? LastReturnUrl { get; private set; }

        public string? LastCode { get; private set; }

        public string? LastState { get; private set; }

        public string? LastError { get; private set; }

        public Guid? LastTenantId { get; private set; }

        public ServiceResult<Uri> RedirectResult { get; set; } = ServiceResult<Uri>.Success(new Uri("https://example.test/redirect?state=external-state"));

        public ServiceResult<SessionTokens> SessionResult { get; set; } = ServiceResult<SessionTokens>.Success(Tokens());

        public Task<ServiceResult<Uri>> CreateChallengeAsync(string provider, string? returnUrl, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastReturnUrl = returnUrl;
            return Task.FromResult(RedirectResult);
        }

        public Task<ServiceResult<Uri>> HandleCallbackAsync(string provider, string? code, string? state, string? error, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastCode = code;
            LastState = state;
            LastError = error;
            return Task.FromResult(RedirectResult);
        }

        public Task<ServiceResult<SessionTokens>> ExchangeAsync(string provider, string? exchangeCode, Guid? tenantId, RequestMetadata metadata, CancellationToken cancellationToken = default)
        {
            LastCode = exchangeCode;
            LastTenantId = tenantId;
            return Task.FromResult(SessionResult);
        }
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public int Calls { get; private set; }

        // Counts audit writes issued by the endpoint under test.
        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default)
        {
            _ = eventType;
            _ = userId;
            _ = tenantId;
            _ = ipAddress;
            _ = userAgent;
            _ = detail;
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.CompletedTask;
        }
    }
}
