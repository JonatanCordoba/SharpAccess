using SharpAccess.Configuration;
using SharpAccess.Middleware;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.UnitTests;

public sealed class HostPipelineIsolationTests
{
    // Verifies that the convenience pipeline does not select host-wide exception or security-header policies by default.
    [Fact]
    public void MiddlewareOptionsAvoidHostWidePoliciesByDefault()
    {
        SharpAccessMiddlewareOptions options = new();

        Assert.False(options.InstallExceptionHandler);
        Assert.False(options.InstallSecurityHeaders);
        Assert.True(options.InstallCookieProtection);
        Assert.True(options.InstallRateLimiter);
        Assert.True(options.InstallAuthentication);
        Assert.True(options.InstallFreshAuthentication);
        Assert.True(options.InstallAuthorization);
    }

    // Verifies that package security headers do not silently select a content security policy.
    [Fact]
    public async Task SecurityHeadersDoNotSelectContentSecurityPolicyByDefault()
    {
        DefaultHttpContext context = Context();
        SecurityHeadersMiddleware middleware = new(
            static httpContext => httpContext.Response.WriteAsync("ok"),
            new SharpAccessSecurityHeadersOptions());

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    // Verifies that a host-selected content security policy is preserved.
    [Fact]
    public async Task SecurityHeadersPreserveHostSelectedContentSecurityPolicy()
    {
        DefaultHttpContext context = Context();
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
        SecurityHeadersMiddleware middleware = new(
            static httpContext => httpContext.Response.WriteAsync("ok"),
            new SharpAccessSecurityHeadersOptions
            {
                ContentSecurityPolicy = "default-src 'self'"
            });

        await middleware.InvokeAsync(context);

        Assert.Equal("default-src 'none'", context.Response.Headers["Content-Security-Policy"].ToString());
    }

    // Creates a response-buffered HTTP context for middleware tests.
    private static DefaultHttpContext Context()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        return context;
    }
}
