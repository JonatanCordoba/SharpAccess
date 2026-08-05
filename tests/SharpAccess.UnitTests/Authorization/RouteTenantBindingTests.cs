using System.Reflection;
using System.Security.Claims;
using SharpAccess.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.UnitTests;

public sealed class RouteTenantBindingTests
{
    private static readonly Guid ActiveTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void TenantMatchesRejectsMissingOrInvalidTenantClaims()
    {
        DefaultHttpContext context = ContextWithRouteValue(ActiveTenantId.ToString("D"));

        Assert.False(TenantMatches(new ClaimsPrincipal(), context));
        Assert.False(TenantMatches(UserWithTenant("not-a-guid"), context));
    }

    [Fact]
    public void TenantMatchesAllowsValidTenantWhenRouteResourceIsUnavailable()
    {
        ClaimsPrincipal user = UserWithTenant(ActiveTenantId.ToString("D"));

        Assert.True(TenantMatches(user, resource: null));
        Assert.True(TenantMatches(user, resource: new object()));
        Assert.True(TenantMatches(user, new DefaultHttpContext()));
    }

    [Theory]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", true)]
    [InlineData("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", false)]
    [InlineData("not-a-guid", false)]
    public void TenantMatchesComparesActiveTenantToRouteValue(string routeValue, bool expected)
    {
        ClaimsPrincipal user = UserWithTenant(ActiveTenantId.ToString("D"));
        DefaultHttpContext context = ContextWithRouteValue(routeValue);

        Assert.Equal(expected, TenantMatches(user, context));
    }

    private static bool TenantMatches(ClaimsPrincipal user, object? resource)
    {
        AuthorizationHandlerContext context = new(
            [new TestRequirement()],
            user,
            resource);
        MethodInfo method = typeof(AttributedEndpointExtensions)
            .GetMethod("TenantMatches", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [context, "tenantId"])!;
    }

    private static ClaimsPrincipal UserWithTenant(string tenantId) =>
        new(new ClaimsIdentity([new Claim(AuthConstants.TenantClaim, tenantId)], authenticationType: "Test"));

    private static DefaultHttpContext ContextWithRouteValue(string routeValue)
    {
        DefaultHttpContext context = new();
        context.Request.RouteValues["tenantId"] = routeValue;
        return context;
    }

    private sealed class TestRequirement : IAuthorizationRequirement
    {
    }
}
