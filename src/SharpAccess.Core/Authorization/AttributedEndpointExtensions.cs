using System.Reflection;
using SharpAccess.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpAccess.Authorization;

/// <summary>Maps Minimal API delegates and applies supported authorization attributes once at startup.</summary>
public static class AttributedEndpointExtensions
{
    private static readonly string[] PatchMethods = [HttpMethods.Patch];

    /// <summary>Maps an attributed GET handler.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="handler">The delegate whose declaring type and method attributes are converted to endpoint authorization metadata.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAttributedGet(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler) => Apply(endpoints.MapGet(pattern, handler), handler);

    /// <summary>Maps an attributed POST handler.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="handler">The delegate whose declaring type and method attributes are converted to endpoint authorization metadata.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAttributedPost(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler) => Apply(endpoints.MapPost(pattern, handler), handler);

    /// <summary>Maps an attributed PUT handler.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="handler">The delegate whose declaring type and method attributes are converted to endpoint authorization metadata.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAttributedPut(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler) => Apply(endpoints.MapPut(pattern, handler), handler);

    /// <summary>Maps an attributed PATCH handler.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="handler">The delegate whose declaring type and method attributes are converted to endpoint authorization metadata.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAttributedPatch(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler) => Apply(endpoints.MapMethods(pattern, PatchMethods, handler), handler);

    /// <summary>Maps an attributed DELETE handler.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="pattern">The route pattern for the endpoint.</param>
    /// <param name="handler">The delegate whose declaring type and method attributes are converted to endpoint authorization metadata.</param>
    /// <returns>A builder for the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapAttributedDelete(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler) => Apply(endpoints.MapDelete(pattern, handler), handler);

    /// <summary>Applies standard and custom metadata by reflecting over the delegate only during endpoint mapping.</summary>
    private static RouteHandlerBuilder Apply(RouteHandlerBuilder builder, Delegate handler)
    {
        Attribute[] attributes = GetAttributes(handler.Method);
        if (attributes.OfType<AllowAnonymousAttribute>().Any())
        {
            return builder.AllowAnonymous();
        }

        AddAuthorizeMetadata(builder, attributes);
        EndpointRequirements requirements = EndpointRequirements.Create(attributes);
        if (!requirements.HasCustomRequirement)
        {
            return builder;
        }

        AuthorizationPolicyBuilder policy = CreatePolicy();
        AddGlobalRequirements(policy, requirements);
        AddTenantRequirements(policy, requirements);
        AddTenantBindingRequirements(policy, requirements);
        return builder.RequireAuthorization(policy.Build());
    }

    private static Attribute[] GetAttributes(MethodInfo method)
    {
        Attribute[] typeAttributes = method.DeclaringType?
            .GetCustomAttributes(inherit: true)
            .OfType<Attribute>()
            .ToArray() ?? [];
        Attribute[] methodAttributes = method
            .GetCustomAttributes(inherit: true)
            .OfType<Attribute>()
            .ToArray();
        return [.. typeAttributes, .. methodAttributes];
    }

    private static void AddAuthorizeMetadata(RouteHandlerBuilder builder, Attribute[] attributes)
    {
        foreach (AuthorizeAttribute authorize in attributes.OfType<AuthorizeAttribute>())
        {
            builder.WithMetadata(authorize);
        }
    }

    private static AuthorizationPolicyBuilder CreatePolicy()
    {
        AuthorizationPolicyBuilder policy = new(AuthConstants.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        return policy;
    }

    private static void AddGlobalRequirements(
        AuthorizationPolicyBuilder policy,
        EndpointRequirements requirements)
    {
        foreach (RequireGlobalPermissionAttribute requirement in requirements.GlobalPermissions)
        {
            policy.RequireClaim(AuthConstants.GlobalPermissionClaim, requirement.Permission);
        }

        foreach (RequireAnyGlobalPermissionAttribute requirement in requirements.AnyGlobalPermissions)
        {
            string[] accepted = requirement.Permissions.ToArray();
            policy.RequireAssertion(context => accepted.Any(permission =>
                context.User.HasClaim(AuthConstants.GlobalPermissionClaim, permission)));
        }

        foreach (RequireAllGlobalPermissionsAttribute requirement in requirements.AllGlobalPermissions)
        {
            string[] required = requirement.Permissions.ToArray();
            policy.RequireAssertion(context => required.All(permission =>
                context.User.HasClaim(AuthConstants.GlobalPermissionClaim, permission)));
        }
    }

    private static void AddTenantRequirements(
        AuthorizationPolicyBuilder policy,
        EndpointRequirements requirements)
    {
        foreach (RequireTenantPermissionAttribute requirement in requirements.TenantPermissions)
        {
            policy.RequireClaim(AuthConstants.TenantPermissionClaim, requirement.Permission);
        }

        foreach (RequireGlobalOrTenantPermissionAttribute requirement in requirements.CrossScopePermissions)
        {
            policy.RequireAssertion(context =>
                context.User.HasClaim(AuthConstants.GlobalPermissionClaim, requirement.GlobalPermission)
                || (context.User.HasClaim(AuthConstants.TenantPermissionClaim, requirement.TenantPermission)
                    && TenantMatches(context, requirements.RouteParameter)));
        }

        foreach (RequireTenantRoleAttribute requirement in requirements.TenantRoles)
        {
            policy.RequireClaim(AuthConstants.TenantRoleClaim, requirement.Role);
        }
    }

    private static void AddTenantBindingRequirements(
        AuthorizationPolicyBuilder policy,
        EndpointRequirements requirements)
    {
        if (requirements.RequiresActiveTenant)
        {
            policy.RequireAssertion(context => TenantMatches(context, requirements.RouteParameter));
        }

        if (requirements.RequiresTenantOwner)
        {
            policy.RequireAssertion(static context => TenantOwnerMatches(context));
        }
    }

    /// <summary>Validates that the tenant claim is present and matches a route tenant when one exists.</summary>
    private static bool TenantMatches(AuthorizationHandlerContext context, string routeParameter)
    {
        if (!TryGetGuidClaim(context, AuthConstants.TenantClaim, out Guid activeTenantId))
        {
            return false;
        }

        if (context.Resource is not HttpContext httpContext
            || !httpContext.Request.RouteValues.TryGetValue(routeParameter, out object? routeValue)
            || routeValue is null)
        {
            return true;
        }

        return Guid.TryParse(
                Convert.ToString(routeValue, System.Globalization.CultureInfo.InvariantCulture),
                out Guid routeTenantId)
            && activeTenantId == routeTenantId;
    }

    /// <summary>Validates that the owner claim is bound to the same active tenant claim.</summary>
    private static bool TenantOwnerMatches(AuthorizationHandlerContext context) =>
        TryGetGuidClaim(context, AuthConstants.TenantClaim, out Guid activeTenantId)
        && TryGetGuidClaim(context, AuthConstants.TenantOwnerClaim, out Guid ownerTenantId)
        && activeTenantId == ownerTenantId;

    private static bool TryGetGuidClaim(
        AuthorizationHandlerContext context,
        string claimType,
        out Guid value) =>
        Guid.TryParse(context.User.FindFirst(claimType)?.Value, out value);

    private sealed record EndpointRequirements(
        RequireGlobalPermissionAttribute[] GlobalPermissions,
        RequireAnyGlobalPermissionAttribute[] AnyGlobalPermissions,
        RequireAllGlobalPermissionsAttribute[] AllGlobalPermissions,
        RequireTenantPermissionAttribute[] TenantPermissions,
        RequireGlobalOrTenantPermissionAttribute[] CrossScopePermissions,
        RequireTenantRoleAttribute[] TenantRoles,
        RequireTenantOwnerAttribute? TenantOwner,
        RequireActiveTenantAttribute? ActiveTenant)
    {
        public string RouteParameter => ActiveTenant?.RouteParameterName ?? "tenantId";

        public bool RequiresTenantOwner => TenantOwner is not null;

        public bool RequiresActiveTenant =>
            ActiveTenant is not null
            || TenantPermissions.Length > 0
            || TenantRoles.Length > 0
            || RequiresTenantOwner;

        public bool HasCustomRequirement =>
            GlobalPermissions.Length
            + AnyGlobalPermissions.Length
            + AllGlobalPermissions.Length
            + TenantPermissions.Length
            + CrossScopePermissions.Length
            + TenantRoles.Length
            + (RequiresTenantOwner ? 1 : 0)
            + (ActiveTenant is null ? 0 : 1) > 0;

        public static EndpointRequirements Create(Attribute[] attributes) => new(
            attributes.OfType<RequireGlobalPermissionAttribute>().ToArray(),
            attributes.OfType<RequireAnyGlobalPermissionAttribute>().ToArray(),
            attributes.OfType<RequireAllGlobalPermissionsAttribute>().ToArray(),
            attributes.OfType<RequireTenantPermissionAttribute>().ToArray(),
            attributes.OfType<RequireGlobalOrTenantPermissionAttribute>().ToArray(),
            attributes.OfType<RequireTenantRoleAttribute>().ToArray(),
            attributes.OfType<RequireTenantOwnerAttribute>().LastOrDefault(),
            attributes.OfType<RequireActiveTenantAttribute>().LastOrDefault());
    }
}
