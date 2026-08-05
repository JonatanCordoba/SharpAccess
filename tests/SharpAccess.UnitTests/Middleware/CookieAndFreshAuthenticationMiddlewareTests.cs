using System.Security.Claims;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class CookieAndFreshAuthenticationMiddlewareTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CookieRequestHeaderMiddlewareRejectsCookieBackedRefreshWithoutConfirmationHeader()
    {
        bool nextCalled = false;
        CookieRequestHeaderMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        AuthOptions options = CookieOptions();
        DefaultHttpContext context = CreateContext(HttpMethods.Post, "/auth/refresh");
        context.Request.Headers.Cookie = $"{options.RefreshTokenCookieName}=refresh-token";

        await middleware.InvokeAsync(context, Options.Create(options));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        string body = await ReadBodyAsync(context);
        Assert.Contains("A request confirmation header is required.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CookieRequestHeaderMiddlewareAllowsCookieBackedLogoutWithConfirmationHeader()
    {
        bool nextCalled = false;
        CookieRequestHeaderMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        AuthOptions options = CookieOptions();
        DefaultHttpContext context = CreateContext(HttpMethods.Post, "/auth/logout");
        context.Request.Headers.Cookie = $"{options.RefreshTokenCookieName}=refresh-token";
        context.Request.Headers[options.CsrfHeaderName] = options.CsrfHeaderValue;

        await middleware.InvokeAsync(context, Options.Create(options));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    // Verifies that removed pre-v1 cookie and header names are no longer interpreted by the package.
    [Fact]
    public async Task CookieRequestHeaderMiddlewareIgnoresRemovedLegacyCookieAndHeader()
    {
        bool nextCalled = false;
        CookieRequestHeaderMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        AuthOptions options = CookieOptions();
        DefaultHttpContext context = CreateContext(HttpMethods.Post, "/auth/logout");
        context.Request.Headers.Cookie = "dotnet_auth_refresh=refresh-token";
        context.Request.Headers["X-DotNetAuth-CSRF"] = options.CsrfHeaderValue;

        await middleware.InvokeAsync(context, Options.Create(options));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false, "POST", "/auth/refresh", true)]
    [InlineData(true, "GET", "/auth/refresh", true)]
    [InlineData(true, "POST", "/auth/me", true)]
    [InlineData(true, "POST", "/auth/refresh", false)]
    public async Task CookieRequestHeaderMiddlewareSkipsRequestsThatDoNotNeedConfirmation(
        bool requireHeader,
        string method,
        string path,
        bool includeCookie)
    {
        bool nextCalled = false;
        CookieRequestHeaderMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        AuthOptions options = CookieOptions();
        options.RequireCsrfHeaderForCookieRefreshRequests = requireHeader;
        DefaultHttpContext context = CreateContext(method, path);
        if (includeCookie)
        {
            context.Request.Headers.Cookie = $"{options.RefreshTokenCookieName}=refresh-token";
        }

        await middleware.InvokeAsync(context, Options.Create(options));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task FreshAuthenticationMiddlewareRejectsAuthenticatedSensitiveMutationWithoutIssuedAt()
    {
        bool nextCalled = false;
        FreshAuthenticationMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext(HttpMethods.Post, "/admin/users");
        context.User = AuthenticatedPrincipal(iat: null);

        await middleware.InvokeAsync(context, Options.Create(FreshOptions()), new FixedClock(Now));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        string body = await ReadBodyAsync(context);
        Assert.Contains("Recent authentication is required.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshAuthenticationMiddlewareRejectsStaleAuthenticatedSensitiveMutation()
    {
        bool nextCalled = false;
        FreshAuthenticationMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext(HttpMethods.Delete, "/tenants/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee/members/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        context.User = AuthenticatedPrincipal(Now.AddMinutes(-11));

        await middleware.InvokeAsync(context, Options.Create(FreshOptions()), new FixedClock(Now));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/admin/users", false)]
    [InlineData("POST", "/auth/login", false)]
    [InlineData("POST", "/auth/revoke", false)]
    [InlineData("PATCH", "/admin/users/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", true)]
    [InlineData("POST", "/auth/change-password", true)]
    public async Task FreshAuthenticationMiddlewareAllowsNonSensitiveAnonymousOrFreshRequests(
        string method,
        string path,
        bool authenticated)
    {
        bool nextCalled = false;
        FreshAuthenticationMiddleware middleware = new(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext(method, path);
        if (authenticated)
        {
            context.User = AuthenticatedPrincipal(Now.AddMinutes(-2));
        }

        await middleware.InvokeAsync(context, Options.Create(FreshOptions()), new FixedClock(Now));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static AuthOptions CookieOptions() => new()
    {
        RequireCsrfHeaderForCookieRefreshRequests = true,
        RefreshTokenCookieName = AuthConstants.DefaultRefreshTokenCookieName,
        RefreshTokenCookiePath = "/auth",
        CsrfHeaderName = AuthConstants.DefaultCsrfHeaderName,
        CsrfHeaderValue = "1"
    };

    private static AuthOptions FreshOptions()
    {
        AuthOptions options = TestOptions.Create();
        options.FreshAuthenticationMinutes = 10;
        return options;
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(DateTimeOffset? iat)
    {
        List<Claim> claims = [];
        if (iat.HasValue)
        {
            claims.Add(new Claim(AuthConstants.AuthenticationTimeClaim, iat.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static DefaultHttpContext CreateContext(string method, string path)
    {
        DefaultHttpContext context = new();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(FreshAuthenticationRequiredMetadata.Instance),
            "unit-test"));
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
