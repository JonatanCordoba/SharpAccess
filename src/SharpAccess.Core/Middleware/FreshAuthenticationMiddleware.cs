using System.Globalization;
using System.Security.Claims;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace SharpAccess.Middleware;

internal sealed class FreshAuthenticationMiddleware(RequestDelegate next)
{
    // Requires recent primary authentication for endpoints explicitly marked as sensitive.
    public async Task InvokeAsync(
        HttpContext context,
        IOptions<AuthOptions> configuredOptions,
        IAuthClock clock)
    {
        if (RequiresFreshAuthentication(context)
            && !HasFreshToken(context.User, configuredOptions.Value, clock.UtcNow))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await AuthProblemDetailsWriter.WriteAsync(
                    context.Response,
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Recent authentication is required.",
                        Type = "https://httpstatuses.com/403"
                    },
                    context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool RequiresFreshAuthentication(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<FreshAuthenticationRequiredMetadata>() is not null;

    private static bool HasFreshToken(ClaimsPrincipal user, AuthOptions options, DateTimeOffset now)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        string? authenticatedAt = user.FindFirstValue(AuthConstants.AuthenticationTimeClaim);
        if (!long.TryParse(authenticatedAt, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
        {
            return false;
        }

        DateTimeOffset primaryAuthenticationUtc;
        try
        {
            primaryAuthenticationUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        TimeSpan age = now - primaryAuthenticationUtc;
        return age >= TimeSpan.Zero
            && age <= TimeSpan.FromMinutes(options.FreshAuthenticationMinutes);
    }
}

// Marker attached only to SharpAccess endpoints that require recent primary authentication.
internal sealed class FreshAuthenticationRequiredMetadata
{
    internal static FreshAuthenticationRequiredMetadata Instance { get; } = new();

    private FreshAuthenticationRequiredMetadata()
    {
    }
}
