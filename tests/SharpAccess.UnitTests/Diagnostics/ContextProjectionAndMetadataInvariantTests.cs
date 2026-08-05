using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Services;
using Microsoft.AspNetCore.Http;

namespace SharpAccess.UnitTests;

public sealed class ContextProjectionAndMetadataInvariantTests
{
    private static readonly string[] TenantRoles = ["tenant-admin"];
    private static readonly string[] TenantPermissions = ["members.read"];

    [Fact]
    public void UserContextProjectsPresentAndAbsentTenantAuthorization()
    {
        Guid tenantId = Guid.NewGuid();
        GlobalAuthorizationContext global = new([], []);
        TenantAuthorizationContext tenant = new(
            tenantId,
            true,
            TenantRoles,
            TenantPermissions);

        UserContext withTenant = new(
            Guid.NewGuid(),
            "person@example.com",
            true,
            new EffectiveAuthorizationContext(global, tenant, 7),
            3);

        Assert.Equal(tenantId, withTenant.TenantId);
        Assert.True(withTenant.IsTenantOwner);
        Assert.Same(TenantRoles, withTenant.TenantRoles);
        Assert.Same(TenantPermissions, withTenant.TenantPermissions);

        UserContext withoutTenant = new(
            Guid.NewGuid(),
            "person@example.com",
            true,
            new EffectiveAuthorizationContext(global, null, 8),
            4);

        Assert.Null(withoutTenant.TenantId);
        Assert.False(withoutTenant.IsTenantOwner);
        Assert.Empty(withoutTenant.TenantRoles);
        Assert.Empty(withoutTenant.TenantPermissions);
    }

    [Fact]
    public void RequestMetadataTruncationDoesNotSplitSurrogatePairs()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.UserAgent =
            new string('a', 511) + char.ConvertFromUtf32(0x1F680);

        RequestMetadata metadata = AuthEndpointRequestMetadata.Metadata(context);

        Assert.Null(metadata.IpAddress);
        Assert.NotNull(metadata.UserAgent);
        Assert.Equal(511, metadata.UserAgent.Length);
        Assert.Equal(new string('a', 511), metadata.UserAgent);
    }

    [Fact]
    public void RequestMetadataTruncationKeepsTheFullLimitForOrdinaryCharacters()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.UserAgent = new string('a', 513);

        RequestMetadata metadata = AuthEndpointRequestMetadata.Metadata(context);

        Assert.Null(metadata.IpAddress);
        Assert.NotNull(metadata.UserAgent);
        Assert.Equal(512, metadata.UserAgent.Length);
        Assert.Equal(new string('a', 512), metadata.UserAgent);
    }
}
