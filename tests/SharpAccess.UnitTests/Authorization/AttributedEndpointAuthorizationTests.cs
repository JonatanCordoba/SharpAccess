using System.Security.Claims;
using SharpAccess;
using SharpAccess.Attributes;
using SharpAccess.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SharpAccess.UnitTests;

public sealed class AttributedEndpointAuthorizationTests
{
    // Verifies that every mapping helper applies standard and explicit global authorization metadata.
    [Fact]
    public void MapAttributedMethodsApplyAllowAnonymousAndAuthorizeMetadata()
    {
        WebApplication app = CreateApp();

        app.MapAttributedGet("/allow", HandlerSet.AllowAnonymous);
        app.MapAttributedPost("/authorize", HandlerSet.AuthorizeOnly);
        app.MapAttributedPut("/permission", HandlerSet.SinglePermission);
        app.MapAttributedPatch("/any", HandlerSet.AnyPermission);
        app.MapAttributedDelete("/all", HandlerSet.AllPermissions);

        RouteEndpoint[] endpoints = RouteEndpoints(app);

        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/allow"
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/authorize"
            && endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>().Count > 0);
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/permission");
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/any");
        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/all");
    }

    // Verifies that explicit global and tenant attributes create authorization policies.
    [Fact]
    public void MapAttributedMethodsApplyPoliciesForPermissionAndTenantAttributes()
    {
        WebApplication app = CreateApp();

        app.MapAttributedGet("/tenant/{tenantId:guid}", HandlerSet.TenantScoped);
        app.MapAttributedPost("/typed", HandlerSet.TypeScoped);
        app.MapAttributedGet("/plain", HandlerSet.Plain);

        RouteEndpoint[] endpoints = RouteEndpoints(app);

        RouteEndpoint tenantEndpoint = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/tenant/{tenantId:guid}");
        Assert.NotNull(tenantEndpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(tenantEndpoint.Metadata.GetMetadata<AuthorizationPolicy>());

        RouteEndpoint typedEndpoint = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/typed");
        Assert.NotNull(typedEndpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(typedEndpoint.Metadata.GetMetadata<AuthorizationPolicy>());

        RouteEndpoint plainEndpoint = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/plain");
        Assert.Null(plainEndpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(plainEndpoint.Metadata.GetMetadata<AuthorizationPolicy>());
    }

    // Verifies that tenant authority in a cross-scope attribute cannot be reused for another route tenant.
    [Trait("MutationInvariant", "AuthorizationFailClosed")]
    [Fact]
    public async Task CrossScopeTenantPermissionIsBoundToRouteTenant()
    {
        WebApplication app = CreateApp();
        app.MapAttributedGet("/cross-scope/{tenantId:guid}", HandlerSet.CrossScope);
        RouteEndpoint endpoint = Assert.Single(
            RouteEndpoints(app),
            candidate => candidate.RoutePattern.RawText == "/cross-scope/{tenantId:guid}");
        AuthorizationPolicy policy = Assert.IsType<AuthorizationPolicy>(
            endpoint.Metadata.GetMetadata<AuthorizationPolicy>());
        IAuthorizationService authorization = app.Services.GetRequiredService<IAuthorizationService>();
        Guid routeTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid otherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        DefaultHttpContext context = new();
        context.Request.RouteValues["tenantId"] = routeTenantId.ToString("D");

        ClaimsPrincipal wrongTenant = Principal(
            new Claim(AuthConstants.TenantClaim, otherTenantId.ToString("D")),
            new Claim(AuthConstants.TenantPermissionClaim, TenantAuthPermissions.MembersRead));
        AuthorizationResult rejected = await authorization.AuthorizeAsync(wrongTenant, context, policy.Requirements);
        Assert.False(rejected.Succeeded);

        ClaimsPrincipal matchingTenant = Principal(
            new Claim(AuthConstants.TenantClaim, routeTenantId.ToString("D")),
            new Claim(AuthConstants.TenantPermissionClaim, TenantAuthPermissions.MembersRead));
        AuthorizationResult accepted = await authorization.AuthorizeAsync(matchingTenant, context, policy.Requirements);
        Assert.True(accepted.Succeeded);
    }

    // Verifies tenant-owner authority is valid only when owner, active, and route tenant identifiers agree.
    [Trait("MutationInvariant", "AuthorizationFailClosed")]
    [Fact]
    public async Task TenantOwnerClaimMustMatchActiveAndRouteTenant()
    {
        WebApplication app = CreateApp();
        app.MapAttributedGet("/owner/{tenantId:guid}", HandlerSet.TenantOwner);
        RouteEndpoint endpoint = Assert.Single(
            RouteEndpoints(app),
            candidate => candidate.RoutePattern.RawText == "/owner/{tenantId:guid}");
        AuthorizationPolicy policy = Assert.IsType<AuthorizationPolicy>(
            endpoint.Metadata.GetMetadata<AuthorizationPolicy>());
        IAuthorizationService authorization = app.Services.GetRequiredService<IAuthorizationService>();
        Guid routeTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid otherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        DefaultHttpContext context = new();
        context.Request.RouteValues["tenantId"] = routeTenantId.ToString("D");

        ClaimsPrincipal matchingOwner = Principal(
            new Claim(AuthConstants.TenantClaim, routeTenantId.ToString("D")),
            new Claim(AuthConstants.TenantOwnerClaim, routeTenantId.ToString("D")));
        AuthorizationResult accepted = await authorization.AuthorizeAsync(
            matchingOwner,
            context,
            policy.Requirements);
        Assert.True(accepted.Succeeded);

        ClaimsPrincipal wrongOwner = Principal(
            new Claim(AuthConstants.TenantClaim, routeTenantId.ToString("D")),
            new Claim(AuthConstants.TenantOwnerClaim, otherTenantId.ToString("D")));
        AuthorizationResult rejectedOwner = await authorization.AuthorizeAsync(
            wrongOwner,
            context,
            policy.Requirements);
        Assert.False(rejectedOwner.Succeeded);

        ClaimsPrincipal malformedOwner = Principal(
            new Claim(AuthConstants.TenantClaim, routeTenantId.ToString("D")),
            new Claim(AuthConstants.TenantOwnerClaim, "not-a-guid"));
        AuthorizationResult rejectedMalformed = await authorization.AuthorizeAsync(
            malformedOwner,
            context,
            policy.Requirements);
        Assert.False(rejectedMalformed.Succeeded);

        ClaimsPrincipal missingTenant = Principal(
            new Claim(AuthConstants.TenantOwnerClaim, routeTenantId.ToString("D")));
        AuthorizationResult rejectedMissingTenant = await authorization.AuthorizeAsync(
            missingTenant,
            context,
            policy.Requirements);
        Assert.False(rejectedMissingTenant.Succeeded);
    }

    // Verifies that inherited type metadata remains supported after compatibility aliases are removed.
    [Fact]
    public void MapAttributedMethodsApplyInheritedTypeMetadata()
    {
        WebApplication app = CreateApp();

        app.MapAttributedGet("/type-allow", TypeAllowAnonymousHandlers.Execute);
        app.MapAttributedGet("/type-authorize", TypeAuthorizeHandlers.Execute);
        app.MapAttributedGet("/type-tenant/{tenantId:guid}", TypeTenantHandlers.Execute);

        RouteEndpoint[] endpoints = RouteEndpoints(app);

        RouteEndpoint anonymousEndpoint = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/type-allow");
        Assert.NotNull(anonymousEndpoint.Metadata.GetMetadata<IAllowAnonymous>());

        RouteEndpoint authorizeEndpoint = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/type-authorize");
        Assert.NotNull(authorizeEndpoint.Metadata.GetMetadata<AuthorizeAttribute>());

        RouteEndpoint tenantEndpoint = Assert.Single(
            endpoints,
            endpoint => endpoint.RoutePattern.RawText == "/type-tenant/{tenantId:guid}");
        AuthorizationPolicy policy = Assert.IsType<AuthorizationPolicy>(
            tenantEndpoint.Metadata.GetMetadata<AuthorizationPolicy>());
        Assert.Contains(AuthConstants.AuthenticationScheme, policy.AuthenticationSchemes);
    }

    // Creates an authenticated principal containing the supplied authorization claims.
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, AuthConstants.AuthenticationScheme));

    // Creates a minimal endpoint-routing application for metadata inspection.
    private static WebApplication CreateApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        return builder.Build();
    }

    // Returns the route endpoints built by the current application.
    private static RouteEndpoint[] RouteEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private sealed class HandlerSet
    {
        // Returns an unprotected endpoint result.
        public static IResult Plain() => Results.Ok();

        // Returns an explicitly anonymous endpoint result.
        [AllowAnonymous]
        public static IResult AllowAnonymous() => Results.Ok();

        // Returns a framework-authorized endpoint result.
        [Authorize]
        public static IResult AuthorizeOnly() => Results.Ok();

        // Returns a result protected by one global permission.
        [RequireGlobalPermission(AuthPermissions.UsersRead)]
        public static IResult SinglePermission() => Results.Ok();

        // Returns a result protected by any accepted global permission.
        [RequireAnyGlobalPermission(AuthPermissions.UsersRead, AuthPermissions.RolesRead)]
        public static IResult AnyPermission() => Results.Ok();

        // Returns a result protected by every required global permission.
        [RequireAllGlobalPermissions(AuthPermissions.UsersRead, AuthPermissions.RolesRead)]
        public static IResult AllPermissions() => Results.Ok();

        // Returns a result protected by explicit global authorization metadata.
        [RequireGlobalPermission(AuthPermissions.UsersRead)]
        public static IResult TypeScoped() => Results.Ok();

        // Returns a result accepting explicit global or route-bound tenant authority.
        [RequireGlobalOrTenantPermission(AuthPermissions.TenantsRead, TenantAuthPermissions.MembersRead)]
        public static IResult CrossScope() => Results.Ok();

        // Returns a result protected by tenant-owner authority.
        [RequireTenantOwner]
        public static IResult TenantOwner() => Results.Ok();

        // Returns a result protected by an active tenant requirement.
        [RequireActiveTenant]
        public static IResult TenantScoped() => Results.Ok();
    }

    [AllowAnonymous]
    private sealed class TypeAllowAnonymousHandlers
    {
        // Returns the attributed endpoint result.
        public static IResult Execute() => Results.Ok();
    }

    [Authenticate]
    private sealed class TypeAuthorizeHandlers
    {
        // Returns the attributed endpoint result.
        public static IResult Execute() => Results.Ok();
    }

    [RequireActiveTenant]
    private sealed class TypeTenantHandlers
    {
        // Returns the attributed endpoint result.
        public static IResult Execute() => Results.Ok();
    }
}
