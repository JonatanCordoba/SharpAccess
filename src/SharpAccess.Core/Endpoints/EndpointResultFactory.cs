using SharpAccess.Domain;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Endpoints;

internal static class EndpointResultFactory
{
    // Converts a service error into a stable sanitized HTTP problem response.
    public static IResult Problem(AuthError error, string? code)
    {
        int status = error switch
        {
            AuthError.InvalidInput => StatusCodes.Status400BadRequest,
            AuthError.Unauthorized => StatusCodes.Status401Unauthorized,
            AuthError.Forbidden => StatusCodes.Status403Forbidden,
            AuthError.NotFound => StatusCodes.Status404NotFound,
            AuthError.Conflict => StatusCodes.Status409Conflict,
            AuthError.Disabled => StatusCodes.Status404NotFound,
            AuthError.ExternalProviderFailure => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        string title = status switch
        {
            StatusCodes.Status400BadRequest => "The request is invalid.",
            StatusCodes.Status401Unauthorized => "Authentication failed.",
            StatusCodes.Status403Forbidden => "Access is denied.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status409Conflict => "The request conflicts with existing data.",
            StatusCodes.Status503ServiceUnavailable => "An external authentication service is unavailable.",
            _ => "An unexpected error occurred."
        };
        return Results.Problem(
            statusCode: status,
            title: title,
            type: $"https://httpstatuses.com/{status}",
            extensions: string.IsNullOrWhiteSpace(code)
                ? null
                : new Dictionary<string, object?> { ["code"] = code });
    }
}
