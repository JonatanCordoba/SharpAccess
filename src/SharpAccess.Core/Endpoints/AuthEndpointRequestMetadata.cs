using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Endpoints;

// Captures bounded request metadata for token and audit persistence.
internal static class AuthEndpointRequestMetadata
{
    // Captures bounded request metadata for refresh-token and audit records.
    internal static RequestMetadata Metadata(HttpContext context) =>
        new(
            Truncate(context.Connection.RemoteIpAddress?.ToString(), 64),
            Truncate(context.Request.Headers.UserAgent.ToString(), 512));

    // Bounds untrusted request metadata before it reaches token or audit persistence.
    private static string? Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length <= maximumLength)
        {
            return value;
        }

        int length = maximumLength;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}

