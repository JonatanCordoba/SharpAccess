using SharpAccess.Domain;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqliteRoleAssignmentInvalidationTests
{
    [Fact]
    public async Task AssignGlobalRoleToUserUpdatesAuthorizationAndInvalidatesSessions()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        RoleRecord role = await context.CreateRoleAsync("Assignment");
        RefreshTokenRecord token = await context.CreateRefreshTokenAsync(user, "assign-role");
        DateTimeOffset changedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1);

        bool changed = await context.Store.AssignGlobalRoleToUserAsync(user.Id, role.Id, changedUtc);

        Assert.True(changed);
        EffectiveAuthorizationContext authorization = await context.Store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);
        Assert.Contains(role.Name, authorization.Global.Roles);
        Assert.Null(authorization.Tenant);
        AuthUser updatedUser = await context.RequireUserAsync(user.Id);
        Assert.Equal(user.SecurityVersion + 1, updatedUser.SecurityVersion);
        await context.AssertRevokedAsync(token.TokenHash, changedUtc);
    }

    [Fact]
    public async Task RemoveGlobalRoleFromUserUpdatesAuthorizationAndInvalidatesSessions()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        RoleRecord role = await context.CreateRoleAsync("Removal");
        Assert.True(await context.Store.AssignGlobalRoleToUserAsync(
            user.Id,
            role.Id,
            SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1)));
        AuthUser assignedUser = await context.RequireUserAsync(user.Id);
        RefreshTokenRecord token = await context.CreateRefreshTokenAsync(assignedUser, "remove-role");
        DateTimeOffset changedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(2);

        bool changed = await context.Store.RemoveGlobalRoleFromUserAsync(user.Id, role.Id, changedUtc);

        Assert.True(changed);
        EffectiveAuthorizationContext authorization = await context.Store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);
        Assert.DoesNotContain(role.Name, authorization.Global.Roles);
        Assert.Null(authorization.Tenant);
        AuthUser updatedUser = await context.RequireUserAsync(user.Id);
        Assert.Equal(assignedUser.SecurityVersion + 1, updatedUser.SecurityVersion);
        await context.AssertRevokedAsync(token.TokenHash, changedUtc);
    }
}
