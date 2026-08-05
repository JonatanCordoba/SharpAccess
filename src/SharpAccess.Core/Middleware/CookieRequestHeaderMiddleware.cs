using SharpAccess.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace SharpAccess.Middleware;

internal sealed class CookieRequestHeaderMiddleware(RequestDelegate next)
{
    // Requires an explicit same-origin header before accepting browser cookie-backed session mutations.
    public async Task InvokeAsync(HttpContext context, IOptions<AuthOptions> configuredOptions)
    {
        AuthOptions options = configuredOptions.Value;
        if (RequiresHeader(context, options) && !HasHeader(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await AuthProblemDetailsWriter.WriteAsync(
                    context.Response,
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "A request confirmation header is required.",
                        Type = "https://httpstatuses.com/403"
                    },
                    context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    // Determines whether the request carries the configured refresh cookie on a mutation path.
    private static bool RequiresHeader(HttpContext context, AuthOptions options) =>
        options.RequireCsrfHeaderForCookieRefreshRequests
        && HttpMethods.IsPost(context.Request.Method)
        && context.Request.Cookies.ContainsKey(options.RefreshTokenCookieName)
        && (MatchesRefreshMutationPath(context.Request.Path, options.RefreshTokenCookiePath, "/refresh")
            || MatchesRefreshMutationPath(context.Request.Path, options.RefreshTokenCookiePath, "/logout"));

    // Determines whether the request path matches a refresh-token mutation endpoint.
    private static bool MatchesRefreshMutationPath(PathString requestPath, string prefix, string endpointPath)
    {
        string normalizedPrefix = string.IsNullOrEmpty(prefix) || string.Equals(prefix, "/", StringComparison.Ordinal)
            ? string.Empty
            : prefix.TrimEnd('/');
        return requestPath.Equals(normalizedPrefix + endpointPath, StringComparison.OrdinalIgnoreCase);
    }

    // Checks whether the configured confirmation header matches the expected value exactly.
    private static bool HasHeader(HttpContext context, AuthOptions options) =>
        context.Request.Headers.TryGetValue(
            options.CsrfHeaderName,
            out Microsoft.Extensions.Primitives.StringValues values)
        && string.Equals(values.ToString(), options.CsrfHeaderValue, StringComparison.Ordinal);
}
