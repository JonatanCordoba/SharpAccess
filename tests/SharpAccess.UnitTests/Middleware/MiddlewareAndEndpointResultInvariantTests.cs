using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace SharpAccess.UnitTests;

public sealed class MiddlewareAndEndpointResultInvariantTests
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    [Theory]
    [InlineData(typeof(BadHttpRequestException), StatusCodes.Status400BadRequest, "The request could not be processed.")]
    [InlineData(typeof(ArgumentException), StatusCodes.Status400BadRequest, "The request could not be processed.")]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden, "The request could not be processed.")]
    [InlineData(typeof(InvalidOperationException), StatusCodes.Status500InternalServerError, "An unexpected error occurred.")]
    public async Task AuthExceptionHandlerWritesSanitizedProblemDetails(
        Type exceptionType,
        int expectedStatus,
        string expectedTitle)
    {
        DefaultHttpContext context = CreateContext();
        AuthExceptionHandler handler = new(NullLogger<AuthExceptionHandler>.Instance);
        Exception exception = CreateException(exceptionType);

        bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        string body = await ReadBodyAsync(context);
        Assert.Contains(expectedTitle, body, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.GetType().Name, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthExceptionHandlerReturnsFalseWhenResponseHasStarted()
    {
        DefaultHttpContext context = CreateStartedContext();
        AuthExceptionHandler handler = new(NullLogger<AuthExceptionHandler>.Instance);

        bool handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.False(handled);
    }

    [Fact]
    public async Task AuthExceptionHandlerUsesClientClosedStatusForAbortedRequests()
    {
        using CancellationTokenSource aborted = new();
        aborted.Cancel();
        DefaultHttpContext context = CreateContext();
        context.RequestAborted = aborted.Token;
        AuthExceptionHandler handler = new(NullLogger<AuthExceptionHandler>.Instance);

        bool handled = await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task StatusCodePagesMiddlewareWritesProblemForBareClientErrors()
    {
        AuthStatusCodePagesMiddleware middleware = new(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        string body = await ReadBodyAsync(context);
        Assert.Contains("The request could not be processed.", body, StringComparison.Ordinal);
        Assert.Contains("https://httpstatuses.com/404", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK, null)]
    [InlineData(StatusCodes.Status400BadRequest, "text/plain")]
    [InlineData(StatusCodes.Status500InternalServerError, null)]
    public async Task StatusCodePagesMiddlewareSkipsResponsesThatShouldNotBeRewritten(
        int statusCode,
        string? contentType)
    {
        AuthStatusCodePagesMiddleware middleware = new(context =>
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            return Task.CompletedTask;
        });
        DefaultHttpContext context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal(contentType, context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task StatusCodePagesMiddlewareSkipsStartedResponses()
    {
        AuthStatusCodePagesMiddleware middleware = new(_ => Task.CompletedTask);
        DefaultHttpContext context = CreateStartedContext(StatusCodes.Status401Unauthorized);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.True(context.Response.HasStarted);
    }

    [Theory]
    [InlineData(1, "invalid", StatusCodes.Status400BadRequest, "The request is invalid.")]
    [InlineData(3, "unauthorized", StatusCodes.Status401Unauthorized, "Authentication failed.")]
    [InlineData(4, "forbidden", StatusCodes.Status403Forbidden, "Access is denied.")]
    [InlineData(5, "missing", StatusCodes.Status404NotFound, "The requested resource was not found.")]
    [InlineData(6, "disabled", StatusCodes.Status404NotFound, "The requested resource was not found.")]
    [InlineData(2, "conflict", StatusCodes.Status409Conflict, "The request conflicts with existing data.")]
    [InlineData(7, "external", StatusCodes.Status503ServiceUnavailable, "An external authentication service is unavailable.")]
    [InlineData(0, null, StatusCodes.Status500InternalServerError, "An unexpected error occurred.")]
    public async Task EndpointResultFactoryMapsErrorsToStableProblemResponses(
        int errorValue,
        string? code,
        int expectedStatus,
        string expectedTitle)
    {
        AuthError error = (AuthError)errorValue;
        DefaultHttpContext context = CreateContext();
        IResult result = EndpointResultFactory.Problem(error, code);

        await result.ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        string body = await ReadBodyAsync(context);
        Assert.Contains(expectedTitle, body, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(code))
        {
            Assert.DoesNotContain("\"code\"", body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(code, body, StringComparison.Ordinal);
        }
    }

    private static DefaultHttpContext CreateContext()
    {
        DefaultHttpContext context = new();
        context.RequestServices = Services;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        return context;
    }

    private static DefaultHttpContext CreateStartedContext(int statusCode = StatusCodes.Status200OK)
    {
        MemoryStream body = new();
        FeatureCollection features = new();
        features.Set<IHttpResponseFeature>(new StartedResponseFeature(body, statusCode));
        DefaultHttpContext context = new(features)
        {
            RequestServices = Services
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        return context;
    }

    private static Exception CreateException(Type exceptionType)
    {
        if (exceptionType == typeof(BadHttpRequestException))
        {
            return new BadHttpRequestException("bad request");
        }

        return (Exception)Activator.CreateInstance(exceptionType, "boom")!;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private sealed class StartedResponseFeature(Stream body, int statusCode) : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = statusCode;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = body;

        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
