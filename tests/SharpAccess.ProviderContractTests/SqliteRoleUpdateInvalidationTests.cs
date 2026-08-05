using SharpAccess.Domain;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqliteRoleUpdateInvalidationTests
{
    [Fact]
    public async Task UpdateHeldRoleInvalidatesSessionsForAffectedUsers()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        RoleRecord role = await context.CreateRoleAsync("Role Update");
        Assert.True(await context.Store.AssignGlobalRoleToUserAsync(
            user.Id,
            role.Id,
            SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1)));
        AuthUser assignedUser = await context.RequireUserAsync(user.Id);
        RefreshTokenRecord token = await context.CreateRefreshTokenAsync(assignedUser, "update-role");
        string updatedName = $"{role.Name} Updated";
        DateTimeOffset changedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(2);

        bool changed = await context.Store.UpdateRoleAsync(
            role.Id,
            updatedName,
            updatedName.ToUpperInvariant(),
            "Updated description.",
            changedUtc);

        Assert.True(changed);
        EffectiveAuthorizationContext authorization = await context.Store.GetEffectiveAuthorizationContextAsync(
            user.Id,
            tenantId: null);
        Assert.Contains(updatedName, authorization.Global.Roles);
        Assert.Null(authorization.Tenant);
        AuthUser updatedUser = await context.RequireUserAsync(user.Id);
        Assert.Equal(assignedUser.SecurityVersion + 1, updatedUser.SecurityVersion);
        await context.AssertRevokedAsync(token.TokenHash, changedUtc);
    }
}
