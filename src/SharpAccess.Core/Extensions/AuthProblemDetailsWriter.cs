using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Extensions.DependencyInjection;

// Writes sanitized ProblemDetails responses for authentication and rate-limit failures.
internal static class AuthProblemDetailsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Replaces the default bearer challenge body with sanitized ProblemDetails.
    internal static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await WriteProblemAsync(
                context.Response,
                new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Authentication is required.",
                    Type = "https://httpstatuses.com/401"
                },
                context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    // Replaces the default bearer forbidden body with sanitized ProblemDetails.
    internal static async Task WriteForbiddenAsync(ForbiddenContext context)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await WriteProblemAsync(
                context.Response,
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Access is forbidden.",
                    Type = "https://httpstatuses.com/403"
                },
                context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    // Writes a sanitized problem response without changing the content type to generic JSON.
    internal static async Task WriteProblemAsync(
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
