using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Middleware;
using SharpAccess.OAuth;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using SharpAccess.Tokens;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharpAccess.UnitTests;

public sealed class ExternalAuthenticationBoundaryInvariantTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 15, 0, 0, TimeSpan.Zero);

    private static readonly IServiceProvider ProblemServices = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    // Verifies that provider exchange bounds remote responses and handles an unknown signing key.
    [Fact]
    public async Task ConfiguredProviderExchangeBoundsResponsesAndHandlesUnknownSigningKeys()
    {
        AuthOptions options = ConfiguredProviderOptions();
        string identityToken = CreateIdentityTokenWithUnknownSigningKey(TestOptions.Google(options).ClientId);
        OAuthRoutingHandler handler = new(identityToken);
        using HttpClient client = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleClientFactory(client),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        OAuthProviderIdentity? identity = await provider.ExchangeAndValidateAsync(
            "google",
            "authorization-code",
            "pkce-verifier",
            "expected-nonce");

        Assert.Null(identity);
        Assert.Equal(1, handler.TokenRequests);
        Assert.InRange(handler.JsonWebKeySetRequests, 1, 2);
    }

    // Verifies that bounded JSON Web Key Sets are cached per configured provider.
    [Fact]
    public async Task ProviderKeyRetrievalCachesBoundedJsonWebKeySets()
    {
        AuthOptions options = ConfiguredProviderOptions();
        JsonResponseHandler handler = new("{\"keys\":[]}");
        using HttpClient client = new(handler);
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleClientFactory(client),
            cache,
            TestOptions.Clock,
            Options.Create(options));

        JsonWebKeySet first = await InvokePrivateAsync<JsonWebKeySet>(
            provider,
            "GetKeysAsync",
            client,
            "google",
            TestOptions.Google(options),
            false,
            CancellationToken.None);
        JsonWebKeySet second = await InvokePrivateAsync<JsonWebKeySet>(
            provider,
            "GetKeysAsync",
            client,
            "google",
            TestOptions.Google(options),
            false,
            CancellationToken.None);

        Assert.Empty(first.Keys);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Requests);
    }

    // Verifies that identity-token validation rejects an unknown signing key.
    [Fact]
    public async Task ProviderIdentityValidationReportsUnknownSigningKeys()
    {
        AuthOptions options = ConfiguredProviderOptions();
        using HttpClient client = new(new JsonResponseHandler("{\"keys\":[]}"));
        using MemoryCache cache = new(new MemoryCacheOptions());
        OpenIdConnectOAuthProvider provider = new(
            new SingleClientFactory(client),
            cache,
            TestOptions.Clock,
            Options.Create(options));
        string identityToken = CreateIdentityTokenWithUnknownSigningKey(TestOptions.Google(options).ClientId);

        TokenValidationResult result = await InvokePrivateAsync<TokenValidationResult>(
            provider,
            "ValidateIdentityTokenAsync",
            identityToken,
            new JsonWebKeySet("{\"keys\":[]}"),
            TestOptions.Google(options));

        Assert.False(result.IsValid);
    }

    // Verifies that bounded content rejects both declared and streaming response overflows.
    [Fact]
    public async Task ProviderBoundedContentRejectsDeclaredAndStreamingOverflows()
    {
        using ByteArrayContent declaredOversize = new(new byte[5]);
        await Assert.ThrowsAsync<InvalidDataException>(() => InvokePrivateAsync<Stream>(
            null,
            "ReadBoundedContentAsync",
            declaredOversize,
            4,
            CancellationToken.None));

        TrackingReadStream successfulSource = new(new byte[] { 1, 2, 3, 4 });
        using StreamContent successfulContent = new(successfulSource);
        Assert.Null(successfulContent.Headers.ContentLength);
        await using Stream copied = await InvokePrivateAsync<Stream>(
            null,
            "ReadBoundedContentAsync",
            successfulContent,
            4,
            CancellationToken.None);
        using MemoryStream successfulDestination = new();
        await copied.CopyToAsync(successfulDestination);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, successfulDestination.ToArray());
        Assert.True(successfulSource.Disposed);

        TrackingReadStream overflowingSource = new(new byte[] { 1, 2, 3, 4, 5 });
        using StreamContent overflowingContent = new(overflowingSource);
        Assert.Null(overflowingContent.Headers.ContentLength);
        await Assert.ThrowsAsync<InvalidDataException>(() => InvokePrivateAsync<Stream>(
            null,
            "ReadBoundedContentAsync",
            overflowingContent,
            4,
            CancellationToken.None));
        Assert.True(overflowingSource.Disposed);
    }

    // Verifies that the OAuth challenge rejects malformed provider state before redirecting.
    [Fact]
    public async Task OAuthChallengeRejectsMalformedProviderState()
    {
        AuthOptions options = ConfiguredProviderOptions();
        RejectingOAuthService service = new();

        IResult malformedChallenge = await AuthEndpointHandlers.OpenIdConnectChallengeAsync(
            "google",
            "/after",
            CreateHttpContext(),
            service,
            Options.Create(options),
            CancellationToken.None);
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            await ExecuteAndGetStatusAsync(malformedChallenge));
    }

    // Verifies missing or mismatched correlation writes one sanitized failure audit before returning 401.
    [Theory]
    [InlineData(null)]
    [InlineData("wrong-state")]
    public async Task OAuthCallbackCorrelationFailureWritesOneSanitizedAudit(string? correlation)
    {
        AuthOptions options = ConfiguredProviderOptions();
        RejectingOAuthService service = new();
        CapturingAuditService audit = new();
        DefaultHttpContext context = CreateHttpContext();
        if (correlation is not null)
        {
            context.Request.Headers.Cookie = $"__Secure-sharpaccess_oidc_google={correlation}";
        }

        using CancellationTokenSource cancellation = new();
        CancellationToken cancellationToken = cancellation.Token;

        IResult result = await AuthEndpointHandlers.OpenIdConnectCallbackAsync(
            "authorization-code",
            "callback-state",
            null,
            context,
            service,
            audit,
            Options.Create(options),
            cancellationToken);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            await ExecuteAndGetStatusAsync(result));
        Assert.Equal(0, service.CallbackCalls);
        Assert.Equal(1, audit.Calls);
        Assert.Equal("oauth_login_failed", audit.EventType);
        string detail = Assert.IsType<string>(audit.Detail);
        Assert.Equal("provider=google;reason=invalid_correlation", detail);
        Assert.DoesNotContain("authorization-code", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("callback-state", detail, StringComparison.Ordinal);
        Assert.Equal(cancellationToken, audit.CancellationToken);
    }

    [Fact]
    public async Task FreshAuthenticationRejectsOutOfRangeAndFutureAuthenticationTimes()
    {
        int nextCalls = 0;
        FreshAuthenticationMiddleware middleware = new(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        AuthOptions options = TestOptions.Create();
        options.FreshAuthenticationMinutes = 10;

        DefaultHttpContext outOfRange = FreshAuthenticationContext(
            long.MaxValue.ToString(CultureInfo.InvariantCulture));
        await middleware.InvokeAsync(outOfRange, Options.Create(options), new FixedClock(Now));
        Assert.Equal(StatusCodes.Status403Forbidden, outOfRange.Response.StatusCode);

        DefaultHttpContext future = FreshAuthenticationContext(
            Now.AddMinutes(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await middleware.InvokeAsync(future, Options.Create(options), new FixedClock(Now));
        Assert.Equal(StatusCodes.Status403Forbidden, future.Response.StatusCode);

        DefaultHttpContext fresh = FreshAuthenticationContext(
            Now.AddMinutes(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await middleware.InvokeAsync(fresh, Options.Create(options), new FixedClock(Now));
        Assert.Equal(StatusCodes.Status200OK, fresh.Response.StatusCode);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public void SessionIssuerOverloadsPreserveAuthenticationTimes()
    {
        AuthOptions options = TestOptions.Create();
        CapturingAccessTokenService accessTokens = new();
        AuthSessionIssuer issuer = new(
            null!,
            accessTokens,
            new FixedTokenProtector(),
            new FixedClock(Now),
            Options.Create(options));
        UserContext context = UserContext();

        AccessTokenResult accessToken = issuer.CreateAccessToken(context);

        Assert.Equal("captured-access-token", accessToken.Token);
        Assert.Equal(Now, accessTokens.AuthenticatedUtc);

        AuthUser user = new(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "person@example.com",
            "PERSON@EXAMPLE.COM",
            "password-hash",
            Now,
            true,
            0,
            null,
            7,
            Now,
            Now);
        Guid familyId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        (string rawToken, RefreshTokenRecord record) = issuer.CreateRefreshToken(
            user,
            familyId,
            new RequestMetadata("192.0.2.10", "coverage-test"),
            Now);

        Assert.Equal("generated-refresh-token", rawToken);
        Assert.Equal(Now, record.AuthenticatedUtc);
        Assert.Equal(Now, record.CreatedUtc);
        Assert.Equal(familyId, record.FamilyId);

        IAuthSessionIssuer defaultOverload = new DefaultOverloadSessionIssuer();
        AccessTokenResult delegated = defaultOverload.CreateAccessToken(
            context,
            Now.AddMinutes(-5));
        Assert.Equal("default-access-token", delegated.Token);
    }

    [Fact]
    public void JwtRejectsFuturePrimaryAuthenticationTime()
    {
        AuthOptions options = TestOptions.Create();
        using JwtAccessTokenService service = new(
            Options.Create(options),
            new FixedClock(Now));
        UserContext user = UserContext();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => service.Create(user, Now.AddSeconds(1)));

        Assert.Equal("authenticatedUtc", exception.ParamName);
    }

    private static UserContext UserContext() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "person@example.com",
        true,
        [],
        [],
        null,
        1);

    // Creates a fully configured provider for external-authentication boundary tests.
    private static AuthOptions ConfiguredProviderOptions()
    {
        AuthOptions options = TestOptions.Create();
        OpenIdConnectProviderOptions google = TestOptions.EnableGoogle(options);
        google.ClientId = "client-id";
        google.ClientSecret = "client-secret-value";
        google.AuthorizationEndpoint = new Uri("https://oauth.example/auth");
        google.TokenEndpoint = new Uri("https://oauth.example/token");
        google.JsonWebKeySetEndpoint = new Uri("https://oauth.example/jwks");
        google.ValidIssuers = ["https://accounts.google.com"];
        google.AllowedHosts = ["oauth.example"];
        return options;
    }

    private static string CreateIdentityTokenWithUnknownSigningKey(string audience)
    {
        using RSA rsa = RSA.Create(2_048);
        RsaSecurityKey signingKey = new(rsa)
        {
            KeyId = "unknown-signing-key"
        };
        DateTime issuedUtc = TestOptions.Now.UtcDateTime;
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = "https://accounts.google.com",
            Audience = audience,
            Subject = new ClaimsIdentity([
                new Claim("sub", "google-subject"),
                new Claim("email", "person@example.com"),
                new Claim("email_verified", "true"),
                new Claim("nonce", "expected-nonce")
            ]),
            IssuedAt = issuedUtc,
            NotBefore = issuedUtc.AddMinutes(-1),
            Expires = issuedUtc.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.RsaSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task<T> InvokePrivateAsync<T>(
        object? target,
        string methodName,
        params object?[] arguments)
    {
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        MethodInfo method = typeof(OpenIdConnectOAuthProvider).GetMethod(methodName, flags)
            ?? throw new InvalidOperationException($"Private method not found: {methodName}");
        object? invocation = method.Invoke(target, arguments);
        Task<T> task = Assert.IsAssignableFrom<Task<T>>(invocation);
        return await task;
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        DefaultHttpContext context = new()
        {
            RequestServices = ProblemServices
        };
        context.Response.Body = new MemoryStream();
        context.Request.Scheme = "https";
        context.SetEndpoint(new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(new OpenIdConnectProviderMetadata("google")),
            "google callback"));
        return context;
    }

    private static async Task<int> ExecuteAndGetStatusAsync(IResult result)
    {
        DefaultHttpContext context = CreateHttpContext();
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private static DefaultHttpContext FreshAuthenticationContext(string authenticatedUtc)
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AuthConstants.AuthenticationTimeClaim, authenticatedUtc)
        ], "Bearer"));
        context.SetEndpoint(new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(FreshAuthenticationRequiredMetadata.Instance),
            "fresh-authentication-test"));
        return context;
    }

    private sealed class CapturingAccessTokenService : IAccessTokenService
    {
        public DateTimeOffset AuthenticatedUtc { get; private set; }

        public AccessTokenResult Create(
            UserContext user,
            DateTimeOffset authenticatedUtc)
        {
            AuthenticatedUtc = authenticatedUtc;
            return new AccessTokenResult("captured-access-token", Now.AddMinutes(5));
        }
    }

    private sealed class FixedTokenProtector : ITokenProtector
    {
        public string Generate(int byteLength = 48) => "generated-refresh-token";

        public string Hash(string rawToken) => "hash:" + rawToken;
    }

    private sealed class DefaultOverloadSessionIssuer : IAuthSessionIssuer
    {
        public Task<ServiceResult<SessionTokens>> IssueSessionAsync(
            AuthUser user,
            Guid? tenantId,
            Guid? familyId,
            RequestMetadata metadata,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UserContext> BuildContextAsync(
            AuthUser user,
            Guid? tenantId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public AccessTokenResult CreateAccessToken(UserContext context) =>
            new("default-access-token", Now.AddMinutes(5));

        public (string RawToken, RefreshTokenRecord Record) CreateRefreshToken(
            AuthUser user,
            Guid familyId,
            RequestMetadata metadata,
            DateTimeOffset now) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(OpenIdConnectOAuthProvider.HttpClientName, name);
            return client;
        }
    }

    private sealed class OAuthRoutingHandler(string identityToken) : HttpMessageHandler
    {
        public int TokenRequests { get; private set; }

        public int JsonWebKeySetRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri == new Uri("https://oauth.example/token"))
            {
                TokenRequests++;
                string json = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["id_token"] = identityToken
                });
                return Task.FromResult(JsonResponse(json, request));
            }

            if (request.Method == HttpMethod.Get
                && request.RequestUri == new Uri("https://oauth.example/jwks"))
            {
                JsonWebKeySetRequests++;
                return Task.FromResult(JsonResponse("{\"keys\":[]}", request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class JsonResponseHandler(string json) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(JsonResponse(json, request));
        }
    }

    private sealed class TrackingReadStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content, writable: false);

        public bool Disposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RejectingOAuthService : IOAuthService
    {
        public int CallbackCalls { get; private set; }

        public Task<ServiceResult<Uri>> CreateChallengeAsync(
            string provider,
            string? returnUrl,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<Uri>.Success(
                new Uri("https://oauth.example/authorize")));

        public Task<ServiceResult<Uri>> HandleCallbackAsync(
            string provider,
            string? code,
            string? state,
            string? error,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CallbackCalls++;
            return Task.FromResult(ServiceResult<Uri>.Failure(
                AuthError.ExternalProviderFailure,
                "unexpected_callback"));
        }

        public Task<ServiceResult<SessionTokens>> ExchangeAsync(
            string provider,
            string? exchangeCode,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingAuditService : IAuditService
    {
        public int Calls { get; private set; }

        public string? EventType { get; private set; }

        public string? Detail { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        // Captures one sanitized endpoint audit and preserves caller cancellation.
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
            _ = userId;
            _ = tenantId;
            _ = ipAddress;
            _ = userAgent;
            Calls++;
            EventType = eventType;
            Detail = detail;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    // Creates a successful JSON response associated with the optional request.
    private static HttpResponseMessage JsonResponse(string json, HttpRequestMessage? request = null) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
        RequestMessage = request
    };
}
