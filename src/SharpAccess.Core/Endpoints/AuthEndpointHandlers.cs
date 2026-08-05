using System.Security.Cryptography;
using System.Text;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.OAuth;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SharpAccess.Endpoints;

internal static class AuthEndpointHandlers
{
    private const string CorrelationCookieNamePrefix = "__Secure-sharpaccess_oidc_";

    // Registers a user and returns a generic anti-enumeration response.
    public static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<string> result = await service.RegisterAsync(
            request.Email,
            request.Password,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Accepted(value: new MessageResponse(result.Value!))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Authenticates a verified email/password account and writes the refresh cookie.
    public static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        IAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        ServiceResult<SessionTokens> result = await service.LoginAsync(
            request.Email,
            request.Password,
            request.TenantId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return AuthEndpointSessionResults.SessionResult(result, httpContext, options.Value);
    }

    // Rotates a refresh token from the secure cookie or explicit non-browser request body.
    public static async Task<IResult> RefreshAsync(
        RefreshRequest? request,
        HttpContext httpContext,
        IAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        string? rawToken = AuthEndpointSessionResults.GetRefreshToken(request?.RefreshToken, httpContext, options.Value);
        ServiceResult<SessionTokens> result = await service.RefreshAsync(
            rawToken,
            request?.TenantId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return AuthEndpointSessionResults.SessionResult(result, httpContext, options.Value);
    }

    // Revokes the current session and clears the browser refresh cookie.
    public static async Task<IResult> LogoutAsync(
        LogoutRequest? request,
        HttpContext httpContext,
        IAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        string? rawToken = AuthEndpointSessionResults.GetRefreshToken(request?.RefreshToken, httpContext, options.Value);
        ServiceResult<bool> result = await service.LogoutAsync(
            userId,
            rawToken,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        AuthEndpointSessionResults.DeleteRefreshCookie(httpContext, options.Value);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Revokes a selected refresh token or its complete family.
    public static async Task<IResult> RevokeAsync(
        RevokeRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        bool canManageSessions = httpContext.User.HasClaim(
            AuthConstants.GlobalPermissionClaim,
            AuthPermissions.SessionsManage);
        ServiceResult<bool> result = await service.RevokeAsync(
            userId,
            canManageSessions,
            request.RefreshToken,
            request.RevokeFamily,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Returns the active profile and explicitly scoped authorization context.
    public static async Task<IResult> MeAsync(
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        Guid? tenantId = AuthEndpointUserContext.TryGetTenantId(httpContext.User);
        ServiceResult<UserContext> result = await service.GetMeAsync(userId, tenantId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        UserContext value = result.Value;
        return Results.Ok(new MeResponse(
            value.Id,
            value.Email,
            value.EmailVerified,
            value.GlobalRoles,
            value.GlobalPermissions,
            value.TenantId,
            value.IsTenantOwner,
            value.TenantRoles,
            value.TenantPermissions,
            value.AuthorizationVersion));
    }

    // Changes the current password and invalidates all existing sessions.
    public static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        if (!AuthEndpointUserContext.TryGetUserId(httpContext.User, out Guid userId))
        {
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "invalid_user");
        }

        ServiceResult<bool> result = await service.ChangePasswordAsync(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Returns a generic password-reset response after optional email delivery.
    public static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<string> result = await service.ForgotPasswordAsync(
            request.Email,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Accepted(value: new MessageResponse(result.Value!))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Resets a password with a single-use token.
    public static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<bool> result = await service.ResetPasswordAsync(
            request.Token,
            request.NewPassword,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Verifies an email with a single-use token.
    public static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<bool> result = await service.VerifyEmailAsync(
            request.Token,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.NoContent()
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Returns a generic response after optionally replacing and sending a verification token.
    public static async Task<IResult> ResendVerificationAsync(
        ResendVerificationRequest request,
        HttpContext httpContext,
        IAuthService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<string> result = await service.ResendVerificationAsync(
            request.Email,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Accepted(value: new MessageResponse(result.Value!))
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Redirects the browser to one configured OpenID Connect authorization endpoint.
    public static async Task<IResult> OpenIdConnectChallengeAsync(
        string provider,
        string? returnUrl,
        HttpContext httpContext,
        IOAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        ServiceResult<Uri> result = await service.CreateChallengeAsync(
            provider,
            returnUrl,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return EndpointResultFactory.Problem(result.Error, result.Code);
        }

        if (!QueryHelpers.ParseQuery(result.Value.Query).TryGetValue("state", out var states)
            || states.Count != 1
            || string.IsNullOrWhiteSpace(states[0]))
        {
            return EndpointResultFactory.Problem(AuthError.ExternalProviderFailure, "oauth_challenge_failed");
        }

        httpContext.Response.Cookies.Append(
            CorrelationCookieName(provider),
            states[0]!,
            CorrelationCookieOptions(options.Value, provider, expires: true));
        return Results.Redirect(result.Value.AbsoluteUri);
    }

    // Handles one configured provider callback and redirects with a short-lived local exchange code.
    public static async Task<IResult> OpenIdConnectCallbackAsync(
        string? code,
        string? state,
        string? error,
        HttpContext httpContext,
        IOAuthService service,
        IAuditService audit,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        string? provider = httpContext.GetEndpoint()?.Metadata.GetMetadata<OpenIdConnectProviderMetadata>()?.Provider;
        if (provider is null
            || !options.Value.OpenIdConnect.Providers.TryGetValue(provider, out OpenIdConnectProviderOptions? configured)
            || !configured.Enabled)
        {
            return EndpointResultFactory.Problem(AuthError.Disabled, "oauth_provider_disabled");
        }

        string cookieName = CorrelationCookieName(provider);
        string? correlation = httpContext.Request.Cookies[cookieName];
        httpContext.Response.Cookies.Delete(
            cookieName,
            CorrelationCookieOptions(options.Value, provider, expires: false));
        RequestMetadata metadata = AuthEndpointRequestMetadata.Metadata(httpContext);
        if (!CorrelationMatches(correlation, state))
        {
            await OAuthAuditWriter.WriteFailureAsync(
                audit,
                provider,
                metadata,
                "invalid_correlation",
                cancellationToken).ConfigureAwait(false);
            return EndpointResultFactory.Problem(AuthError.Unauthorized, "oauth_callback_failed");
        }

        ServiceResult<Uri> result = await service.HandleCallbackAsync(
            provider,
            code,
            state,
            error,
            metadata,
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? Results.Redirect(result.Value!.AbsoluteUri)
            : EndpointResultFactory.Problem(result.Error, result.Code);
    }

    // Builds the provider-keyed correlation cookie name.
    private static string CorrelationCookieName(string provider) => CorrelationCookieNamePrefix + provider;

    // Creates a secure callback-scoped correlation cookie with bounded lifetime.
    private static CookieOptions CorrelationCookieOptions(AuthOptions options, string provider, bool expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = options.OpenIdConnect.Providers[provider].CallbackPath,
        MaxAge = expires ? TimeSpan.FromMinutes(options.OAuthStateMinutes) : null
    };

    // Compares correlation state in constant time without accepting malformed values.
    private static bool CorrelationMatches(string? correlation, string? state)
    {
        if (string.IsNullOrWhiteSpace(correlation)
            || string.IsNullOrWhiteSpace(state)
            || correlation.Length > 1_024
            || state.Length > 1_024)
        {
            return false;
        }

        byte[] correlationDigest = SHA256.HashData(Encoding.UTF8.GetBytes(correlation));
        byte[] stateDigest = SHA256.HashData(Encoding.UTF8.GetBytes(state));
        return CryptographicOperations.FixedTimeEquals(correlationDigest, stateDigest);
    }

    // Exchanges the callback's one-time code for a local session.
    public static async Task<IResult> OpenIdConnectExchangeAsync(
        string provider,
        OAuthExchangeRequest request,
        HttpContext httpContext,
        IOAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        ServiceResult<SessionTokens> result = await service.ExchangeAsync(
            provider,
            request.Code,
            request.TenantId,
            AuthEndpointRequestMetadata.Metadata(httpContext),
            cancellationToken).ConfigureAwait(false);
        return AuthEndpointSessionResults.SessionResult(result, httpContext, options.Value);
    }
}

internal sealed record OpenIdConnectProviderMetadata(string Provider);
