using SharpAccess.Configuration;
using SharpAccess.Domain;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.Endpoints;

// Converts issued sessions into endpoint responses and manages refresh-token cookie transport.
internal static class AuthEndpointSessionResults
{
    // Converts successful session issuance into JSON plus a secure refresh cookie.
    internal static IResult SessionResult(
        ServiceResult<SessionTokens> result,
        HttpContext httpContext,
        AuthOptions options)
    {
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        SessionTokens session = result.Value;
        if (!string.IsNullOrEmpty(session.RefreshToken))
        {
            WriteRefreshCookie(httpContext, options, session.RefreshToken, session.RefreshTokenExpiresUtc);
        }

        return Results.Ok(new TokenResponse(
            session.AccessToken,
            session.AccessTokenExpiresUtc,
            "Bearer",
            options.ReturnRefreshTokenInResponseBody && !string.IsNullOrEmpty(session.RefreshToken)
                ? session.RefreshToken
                : null,
            options.ReturnRefreshTokenInResponseBody && !string.IsNullOrEmpty(session.RefreshToken)
                ? session.RefreshTokenExpiresUtc
                : null));
    }

    // Reads an explicit refresh token only when response-body transport is enabled; otherwise uses the configured cookie.
    internal static string? GetRefreshToken(string? requestToken, HttpContext context, AuthOptions options)
    {
        if (options.ReturnRefreshTokenInResponseBody && !string.IsNullOrWhiteSpace(requestToken))
        {
            return requestToken;
        }

        return context.Request.Cookies.TryGetValue(options.RefreshTokenCookieName, out string? cookie)
            ? cookie
            : null;
    }

    // Deletes the configured refresh cookie with the same transport attributes used when it was issued.
    internal static void DeleteRefreshCookie(HttpContext context, AuthOptions options) =>
        context.Response.Cookies.Delete(
            options.RefreshTokenCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = options.RefreshCookieSecurePolicy == CookieSecurePolicy.Always
                    || (options.RefreshCookieSecurePolicy == CookieSecurePolicy.SameAsRequest && context.Request.IsHttps),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = options.RefreshTokenCookiePath
            });

    // Writes the browser-safe refresh token cookie.
    private static void WriteRefreshCookie(
        HttpContext context,
        AuthOptions options,
        string token,
        DateTimeOffset expiresUtc)
    {
        context.Response.Cookies.Append(
            options.RefreshTokenCookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = options.RefreshCookieSecurePolicy switch
                {
                    CookieSecurePolicy.Always => true,
                    CookieSecurePolicy.SameAsRequest => context.Request.IsHttps,
                    _ => false
                },
                SameSite = SameSiteMode.Lax,
                Expires = expiresUtc,
                IsEssential = true,
                Path = options.RefreshTokenCookiePath
            });
    }
}
