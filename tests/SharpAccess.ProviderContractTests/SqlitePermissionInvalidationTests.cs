using SharpAccess;
using SharpAccess.Domain;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqlitePermissionInvalidationTests
{
    [Fact]
    public async Task AssignPermissionToHeldRoleUpdatesAuthorizationAndInvalidatesSessions()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        RoleRecord role = await context.CreateRoleAsync("Permission Assignment");
        Assert.True(await context.Store.AssignGlobalRoleToUserAsync(
            user.Id,
            role.Id,
            SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1)));
        AuthUser assignedUser = await context.RequireUserAsync(user.Id);
        RefreshTokenRecord token = await context.CreateRefreshTokenAsync(assignedUser, "assign-permission");
        PermissionRecord permission = await context.FindPermissionAsync(AuthPermissions.AuditRead);
        DateTimeOffset changedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(2);

        bool changed = await context.Store.AssignPermissionToRoleAsync(role.Id, permission.Id, changedUtc);

        Assert.True(changed);
        EffectiveAuthorizationContext authorization = await context.Store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);
        Assert.Contains(AuthPermissions.AuditRead, authorization.Global.Permissions);
        Assert.Null(authorization.Tenant);
        AuthUser updatedUser = await context.RequireUserAsync(user.Id);
        Assert.Equal(assignedUser.SecurityVersion + 1, updatedUser.SecurityVersion);
        await context.AssertRevokedAsync(token.TokenHash, changedUtc);
    }

    [Fact]
    public async Task RemovePermissionFromHeldRoleUpdatesAuthorizationAndInvalidatesSessions()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        RoleRecord role = await context.CreateRoleAsync("Permission Removal");
        PermissionRecord permission = await context.FindPermissionAsync(AuthPermissions.AuditRead);
        Assert.True(await context.Store.AssignGlobalRoleToUserAsync(
            user.Id,
            role.Id,
            SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1)));
        Assert.True(await context.Store.AssignPermissionToRoleAsync(
            role.Id,
            permission.Id,
            SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(2)));
        AuthUser assignedUser = await context.RequireUserAsync(user.Id);
        RefreshTokenRecord token = await context.CreateRefreshTokenAsync(assignedUser, "remove-permission");
        DateTimeOffset changedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(3);

        bool changed = await context.Store.RemovePermissionFromRoleAsync(role.Id, permission.Id, changedUtc);

        Assert.True(changed);
        EffectiveAuthorizationContext authorization = await context.Store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);
        Assert.DoesNotContain(AuthPermissions.AuditRead, authorization.Global.Permissions);
        Assert.Null(authorization.Tenant);
        AuthUser updatedUser = await context.RequireUserAsync(user.Id);
        Assert.Equal(assignedUser.SecurityVersion + 1, updatedUser.SecurityVersion);
        await context.AssertRevokedAsync(token.TokenHash, changedUtc);
    }
}
