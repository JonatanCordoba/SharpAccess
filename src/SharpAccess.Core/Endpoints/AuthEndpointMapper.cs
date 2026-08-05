using System.Text.Json;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Middleware;
using SharpAccess.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace SharpAccess.Endpoints;

internal static class AuthEndpointMapper
{
    internal const string LoginRateLimit = "SharpAccess.Login";
    internal const string RegisterRateLimit = "SharpAccess.Register";
    internal const string RefreshRateLimit = "SharpAccess.Refresh";
    internal const string PasswordResetRateLimit = "SharpAccess.PasswordReset";
    internal const string VerificationRateLimit = "SharpAccess.Verification";
    internal const string OAuthRateLimit = "SharpAccess.OAuth";

    private const string DefaultRefreshTokenCookiePath = "/auth";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Maps enabled authentication, administration, and tenant endpoint groups.
    public static RouteGroupBuilder Map(
        IEndpointRouteBuilder endpoints,
        string prefix,
        AuthOptions options)
    {
        bool hasExternalAuthentication = options.OpenIdConnect.Providers.Values.Any(
            static provider => provider?.Enabled == true);
        EnsureOpenIdConnectCallbacksDoNotCollide(options, prefix, hasExternalAuthentication);
        AlignRefreshCookiePathWithEndpointPrefix(options, prefix);
        RouteGroupBuilder auth = endpoints.MapGroup(prefix).WithTags("Authentication");
        if (options.Features.Registration)
        {
            auth.MapPost("/register", AuthEndpointHandlers.RegisterAsync)
                .AllowAnonymous()
                .RequireRateLimiting(RegisterRateLimit);
            auth.MapPost("/verify-email", AuthEndpointHandlers.VerifyEmailAsync)
                .AllowAnonymous()
                .RequireRateLimiting(VerificationRateLimit);
            auth.MapPost("/resend-verification", AuthEndpointHandlers.ResendVerificationAsync)
                .AllowAnonymous()
                .RequireRateLimiting(VerificationRateLimit);
        }

        if (options.Features.PasswordAuthentication)
        {
            auth.MapPost("/login", LoginAsync)
                .AllowAnonymous()
                .RequireRateLimiting(LoginRateLimit);
            auth.MapPost("/change-password", AuthEndpointHandlers.ChangePasswordAsync)
                .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
                .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.ProfileUpdate));
        }

        if (options.Features.PasswordReset)
        {
            auth.MapPost("/forgot-password", AuthEndpointHandlers.ForgotPasswordAsync)
                .AllowAnonymous()
                .RequireRateLimiting(PasswordResetRateLimit);
            auth.MapPost("/reset-password", AuthEndpointHandlers.ResetPasswordAsync)
                .AllowAnonymous()
                .RequireRateLimiting(PasswordResetRateLimit);
        }

        if (options.Features.RefreshTokens)
        {
            auth.MapPost("/refresh", AuthEndpointHandlers.RefreshAsync)
                .AllowAnonymous()
                .RequireRateLimiting(RefreshRateLimit);
            auth.MapPost("/logout", AuthEndpointHandlers.LogoutAsync)
                .RequireAuthorization(AuthPolicy());
            auth.MapPost("/revoke", AuthEndpointHandlers.RevokeAsync)
                .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
                .RequireAuthorization(AuthPolicy());
        }

        if (options.Features.PasswordAuthentication || hasExternalAuthentication)
        {
            auth.MapGet("/me", AuthEndpointHandlers.MeAsync)
                .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.ProfileRead));
        }

        if (hasExternalAuthentication)
        {
            auth.MapGet("/oauth/{provider}/challenge", AuthEndpointHandlers.OpenIdConnectChallengeAsync)
                .AllowAnonymous()
                .RequireRateLimiting(OAuthRateLimit);
            auth.MapPost("/oauth/{provider}/exchange", AuthEndpointHandlers.OpenIdConnectExchangeAsync)
                .AllowAnonymous()
                .RequireRateLimiting(OAuthRateLimit);
            foreach ((string providerName, OpenIdConnectProviderOptions provider) in options.OpenIdConnect.Providers
                         .Where(static pair => pair.Value?.Enabled == true))
            {
                endpoints.MapGet(provider.CallbackPath, AuthEndpointHandlers.OpenIdConnectCallbackAsync)
                    .WithMetadata(new OpenIdConnectProviderMetadata(providerName))
                    .WithTags("Authentication")
                    .AllowAnonymous()
                    .RequireRateLimiting(OAuthRateLimit);
            }
        }

        if (options.Features.Administration)
        {
            MapAdministration(endpoints);
        }

        if (options.Features.Tenancy)
        {
            MapTenants(endpoints);
        }

        return auth;
    }

    // Rejects enabled callbacks that overlap routes mapped for the selected authentication prefix.
    private static void EnsureOpenIdConnectCallbacksDoNotCollide(
        AuthOptions options,
        string prefix,
        bool hasExternalAuthentication)
    {
        if (!hasExternalAuthentication)
        {
            return;
        }

        List<string> mappedRoutes = [];
        if (options.Features.Registration)
        {
            mappedRoutes.AddRange(
            [
                $"{prefix}/register",
                $"{prefix}/verify-email",
                $"{prefix}/resend-verification"
            ]);
        }

        if (options.Features.PasswordAuthentication)
        {
            mappedRoutes.Add($"{prefix}/login");
            mappedRoutes.Add($"{prefix}/change-password");
        }

        if (options.Features.PasswordReset)
        {
            mappedRoutes.Add($"{prefix}/forgot-password");
            mappedRoutes.Add($"{prefix}/reset-password");
        }

        if (options.Features.RefreshTokens)
        {
            mappedRoutes.Add($"{prefix}/refresh");
            mappedRoutes.Add($"{prefix}/logout");
            mappedRoutes.Add($"{prefix}/revoke");
        }

        if (options.Features.PasswordAuthentication || hasExternalAuthentication)
        {
            mappedRoutes.Add($"{prefix}/me");
        }

        mappedRoutes.Add($"{prefix}/oauth/{{provider}}/challenge");
        mappedRoutes.Add($"{prefix}/oauth/{{provider}}/exchange");

        if (options.Features.Administration)
        {
            mappedRoutes.AddRange(
            [
                "/admin/users",
                "/admin/users/{userId:guid}/status",
                "/admin/roles",
                "/admin/roles/{roleId:guid}",
                "/admin/permissions",
                "/admin/roles/{roleId:guid}/permissions",
                "/admin/roles/{roleId:guid}/permissions/{permissionId:guid}",
                "/admin/users/{userId:guid}/roles",
                "/admin/users/{userId:guid}/roles/{roleId:guid}",
                "/admin/audit-logs"
            ]);
        }

        if (options.Features.Tenancy)
        {
            mappedRoutes.AddRange(
            [
                "/tenants",
                "/tenants/{tenantId:guid}",
                "/tenants/{tenantId:guid}/owner",
                "/tenants/{tenantId:guid}/owner/transfer",
                "/tenants/{tenantId:guid}/members",
                "/tenants/{tenantId:guid}/members/{userId:guid}/roles"
            ]);
        }

        foreach (OpenIdConnectProviderOptions provider in options.OpenIdConnect.Providers.Values
                     .Where(static provider => provider?.Enabled == true))
        {
            if (mappedRoutes.Any(pattern =>
                    AuthOptionsValidator.RoutePatternMatchesPath(pattern, provider.CallbackPath)))
            {
                throw new InvalidOperationException(
                    "An enabled OpenIdConnect callback path collides with a SharpAccess route mapped for the selected endpoint prefix.");
            }
        }
    }

    // Maps global permission-protected administration operations.
    private static void MapAdministration(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder admin = endpoints.MapGroup("/admin").WithTags("Administration");
        admin.MapGet("/users", AdminEndpointHandlers.ListUsersAsync)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.UsersRead));
        admin.MapPatch("/users/{userId:guid}/status", AdminEndpointHandlers.SetUserStatusAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.UsersManage));
        admin.MapGet("/roles", AdminEndpointHandlers.ListRolesAsync)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.RolesRead));
        admin.MapPost("/roles", AdminEndpointHandlers.CreateRoleAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.RolesManage));
        admin.MapPatch("/roles/{roleId:guid}", AdminEndpointHandlers.UpdateRoleAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.RolesManage));
        admin.MapGet("/permissions", AdminEndpointHandlers.ListPermissionsAsync)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.PermissionsRead));
        admin.MapPost("/roles/{roleId:guid}/permissions", AdminEndpointHandlers.AssignPermissionAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.PermissionsManage));
        admin.MapDelete(
                "/roles/{roleId:guid}/permissions/{permissionId:guid}",
                AdminEndpointHandlers.RemovePermissionAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.PermissionsManage));
        admin.MapPost("/users/{userId:guid}/roles", AdminEndpointHandlers.AssignUserRoleAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.UsersManage));
        admin.MapDelete("/users/{userId:guid}/roles/{roleId:guid}", AdminEndpointHandlers.RemoveUserRoleAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.UsersManage));
        admin.MapGet("/audit-logs", AdminEndpointHandlers.ListAuditAsync)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.AuditRead));
    }

    // Maps tenant operations with active-tenant and tenant-permission enforcement.
    private static void MapTenants(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder tenants = endpoints.MapGroup("/tenants").WithTags("Tenants");
        tenants.MapPost("/", TenantEndpointHandlers.CreateAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(GlobalPermissionPolicy(AuthPermissions.TenantsManage));
        tenants.MapGet("/", TenantEndpointHandlers.ListAsync)
            .RequireAuthorization(AuthPolicy());
        tenants.MapGet("/{tenantId:guid}", TenantEndpointHandlers.GetAsync)
            .RequireAuthorization(GlobalOrTenantPermissionPolicy(
                AuthPermissions.TenantsRead,
                TenantAuthPermissions.TenantRead));
        tenants.MapGet("/{tenantId:guid}/owner", TenantEndpointHandlers.GetOwnerAsync)
            .RequireAuthorization(TenantPermissionPolicy(TenantAuthPermissions.MembersRead));
        tenants.MapPost("/{tenantId:guid}/owner/transfer", TenantEndpointHandlers.TransferOwnershipAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(TenantOwnerPolicy(TenantAuthPermissions.OwnershipTransfer));
        tenants.MapPost("/{tenantId:guid}/members", TenantEndpointHandlers.AddMemberAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(TenantPermissionPolicy(TenantAuthPermissions.MembersManage));
        tenants.MapGet("/{tenantId:guid}/members", TenantEndpointHandlers.ListMembersAsync)
            .RequireAuthorization(TenantPermissionPolicy(TenantAuthPermissions.MembersRead));
        tenants.MapPost(
                "/{tenantId:guid}/members/{userId:guid}/roles",
                TenantEndpointHandlers.AssignMemberRoleAsync)
            .WithMetadata(FreshAuthenticationRequiredMetadata.Instance)
            .RequireAuthorization(TenantPermissionPolicy(TenantAuthPermissions.RolesManage));
    }

    // Authenticates a login request after explicit JSON parsing so malformed payloads receive ProblemDetails.
    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        IAuthService service,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        LoginRequest? request = await ReadJsonBodyAsync<LoginRequest>(httpContext, cancellationToken).ConfigureAwait(false);
        return request is null
            ? EndpointResultFactory.Problem(AuthError.InvalidInput, "malformed_json")
            : await AuthEndpointHandlers.LoginAsync(
                request,
                httpContext,
                service,
                options,
                cancellationToken).ConfigureAwait(false);
    }

    // Reads one JSON request body and converts parsing failures into a null payload.
    private static async Task<TRequest?> ReadJsonBodyAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<TRequest>(
                    context.Request.Body,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BadHttpRequestException)
        {
            return default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    // Creates the package-scheme authenticated policy without changing host defaults.
    private static AuthorizationPolicy AuthPolicy() =>
        new AuthorizationPolicyBuilder(AuthConstants.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

    // Creates a package-scheme global permission policy.
    private static AuthorizationPolicy GlobalPermissionPolicy(string permission) =>
        new AuthorizationPolicyBuilder(AuthConstants.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthConstants.GlobalPermissionClaim, permission)
            .Build();

    // Creates a deliberate global-or-active-tenant policy for a route that supports both authority sources.
    private static AuthorizationPolicy GlobalOrTenantPermissionPolicy(
        string globalPermission,
        string tenantPermission) =>
        new AuthorizationPolicyBuilder(AuthConstants.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                context.User.HasClaim(AuthConstants.GlobalPermissionClaim, globalPermission)
                || (context.User.HasClaim(AuthConstants.TenantPermissionClaim, tenantPermission)
                    && TenantMatchesRoute(context)))
            .Build();

    // Creates a package-scheme active-tenant permission policy bound to the tenantId route value.
    private static AuthorizationPolicy TenantPermissionPolicy(string permission) =>
        new AuthorizationPolicyBuilder(AuthConstants.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthConstants.TenantPermissionClaim, permission)
            .RequireAssertion(static context => TenantMatchesRoute(context))
            .Build();

    // Creates an owner-only active-tenant permission policy bound to the tenantId route value.
    private static AuthorizationPolicy TenantOwnerPolicy(string permission) =>
        new AuthorizationPolicyBuilder(AuthConstants.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(AuthConstants.TenantPermissionClaim, permission)
            .RequireAssertion(static context => TenantMatchesRoute(context) && TenantOwnerMatches(context))
            .Build();

    // Keeps refresh-cookie transport aligned with the mapped refresh and logout endpoints.
    private static void AlignRefreshCookiePathWithEndpointPrefix(AuthOptions options, string endpointPrefix)
    {
        if (!options.Features.RefreshTokens)
        {
            return;
        }

        string effectiveEndpointPrefix = string.IsNullOrEmpty(endpointPrefix) ? "/" : endpointPrefix;
        string normalizedCookiePath = NormalizeRefreshCookiePath(options.RefreshTokenCookiePath);
        if (string.Equals(normalizedCookiePath, DefaultRefreshTokenCookiePath, StringComparison.Ordinal)
            && !string.Equals(effectiveEndpointPrefix, DefaultRefreshTokenCookiePath, StringComparison.Ordinal))
        {
            options.RefreshTokenCookiePath = effectiveEndpointPrefix;
            return;
        }

        if (!string.Equals(normalizedCookiePath, effectiveEndpointPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "RefreshTokenCookiePath must match the mapped SharpAccess endpoint prefix when refresh-token cookies are enabled.");
        }

        options.RefreshTokenCookiePath = normalizedCookiePath;
    }

    // Normalizes a validated local cookie path while preserving the root path.
    private static string NormalizeRefreshCookiePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Length > 1_024
            || !path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains('\\')
            || path.Contains('?')
            || path.Contains('#')
            || path.Contains(';')
            || path.Any(char.IsControl))
        {
            throw new InvalidOperationException("RefreshTokenCookiePath must be a bounded local absolute path.");
        }

        return path.Length == 1 ? "/" : path.TrimEnd('/');
    }

    // Requires the active tenant claim to equal the tenantId route value.
    private static bool TenantMatchesRoute(AuthorizationHandlerContext context)
    {
        string? tenantClaim = context.User.FindFirst(AuthConstants.TenantClaim)?.Value;
        if (!Guid.TryParse(tenantClaim, out Guid activeTenantId)
            || context.Resource is not HttpContext httpContext
            || !httpContext.Request.RouteValues.TryGetValue("tenantId", out object? routeValue)
            || routeValue is null)
        {
            return false;
        }

        return Guid.TryParse(
                Convert.ToString(routeValue, System.Globalization.CultureInfo.InvariantCulture),
                out Guid routeTenantId)
            && routeTenantId == activeTenantId;
    }

    // Requires the tenant owner claim to equal the active tenant claim.
    private static bool TenantOwnerMatches(AuthorizationHandlerContext context)
    {
        string? tenantClaim = context.User.FindFirst(AuthConstants.TenantClaim)?.Value;
        string? ownerClaim = context.User.FindFirst(AuthConstants.TenantOwnerClaim)?.Value;
        return Guid.TryParse(tenantClaim, out Guid activeTenantId)
            && Guid.TryParse(ownerClaim, out Guid ownerTenantId)
            && activeTenantId == ownerTenantId;
    }
}
