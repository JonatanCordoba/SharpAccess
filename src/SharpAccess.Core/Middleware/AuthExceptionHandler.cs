using System.Text.Json;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SharpAccess.Middleware;

internal static class AuthProblemDetailsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Writes a sanitized RFC 7807 response while preserving the problem JSON media type.
    public static async Task WriteAsync(
        HttpResponse response,
        ProblemDetails problem,
        CancellationToken cancellationToken)
    {
        response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
                response.Body,
                problem,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class AuthExceptionBoundaryMiddleware(RequestDelegate next)
{
    // Catches downstream exceptions only when the host explicitly installs the SharpAccess exception boundary.
    public async Task InvokeAsync(HttpContext context, ILogger<AuthExceptionHandler> logger)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AuthExceptionHandler handler = new(logger);
            bool handled = await handler.TryHandleAsync(
                    context,
                    exception,
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (!handled)
            {
                throw;
            }
        }
    }
}

internal sealed class AuthExceptionHandler(ILogger<AuthExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, Exception?> LogUnhandledException = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(1, nameof(LogUnhandledException)),
        "An unhandled authentication component exception occurred.");

    private static readonly Action<ILogger, int, Exception?> LogRejectedRequest = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(2, nameof(LogRejectedRequest)),
        "A request was rejected with status {StatusCode}.");

    // Converts expected request failures and unexpected exceptions into sanitized ProblemDetails responses.
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        int statusCode = exception switch
        {
            BadHttpRequestException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        if (statusCode >= 500)
        {
            LogUnhandledException(logger, exception);
        }
        else
        {
            LogRejectedRequest(logger, statusCode, null);
        }

        ProblemDetails problem = CreateProblem(statusCode);
        problem.Title = statusCode == StatusCodes.Status500InternalServerError
            ? "An unexpected error occurred."
            : "The request could not be processed.";
        httpContext.Response.StatusCode = statusCode;
        await AuthProblemDetailsWriter.WriteAsync(httpContext.Response, problem, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static ProblemDetails CreateProblem(int statusCode) => new()
    {
        Status = statusCode,
        Type = $"https://httpstatuses.com/{statusCode}"
    };
}

internal sealed class AuthStatusCodePagesMiddleware(RequestDelegate next)
{
    // Adds sanitized ProblemDetails for framework-generated bare client error responses.
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context).ConfigureAwait(false);
        if (context.Response.HasStarted
            || !string.IsNullOrEmpty(context.Response.ContentType)
            || context.Response.StatusCode < StatusCodes.Status400BadRequest
            || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return;
        }

        await AuthProblemDetailsWriter.WriteAsync(
                context.Response,
                new ProblemDetails
                {
                    Status = context.Response.StatusCode,
                    Title = "The request could not be processed.",
                    Type = $"https://httpstatuses.com/{context.Response.StatusCode}"
                },
                context.RequestAborted)
            .ConfigureAwait(false);
    }
}

internal sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    SharpAccessSecurityHeadersOptions options)
{
    // Adds only explicitly configured security headers without changing host CORS or HTTPS policy.
    public async Task InvokeAsync(HttpContext context)
    {
        AddHeader(context.Response, "X-Content-Type-Options", options.ContentTypeOptions);
        AddHeader(context.Response, "X-Frame-Options", options.FrameOptions);
        AddHeader(context.Response, "Referrer-Policy", options.ReferrerPolicy);
        AddHeader(context.Response, "Permissions-Policy", options.PermissionsPolicy);
        AddHeader(context.Response, "Content-Security-Policy", options.ContentSecurityPolicy);

        await next(context).ConfigureAwait(false);
    }

    // Adds one configured header without replacing a value already selected by the host.
    private static void AddHeader(HttpResponse response, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            response.Headers.TryAdd(name, value);
        }
    }
}
