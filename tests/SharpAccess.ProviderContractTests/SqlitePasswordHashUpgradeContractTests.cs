using SharpAccess.Domain;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]

public sealed class SqlitePasswordHashUpgradeContractTests
{
    [Fact]
    public async Task PasswordHashUpgradeUsesExpectedHashAndSecurityVersion()
    {
        await using SqliteAuthorizationInvalidationTestContext context = new();
        AuthUser user = await context.CreateUserAsync();
        DateTimeOffset updatedUtc = SqliteAuthorizationInvalidationTestContext.Now.AddMinutes(1);

        bool updated = await context.Store.UpdatePasswordHashAsync(
            user.Id,
            user.PasswordHash!,
            user.SecurityVersion,
            "upgraded-hash",
            updatedUtc);

        Assert.True(updated);
        AuthUser persisted = await context.RequireUserAsync(user.Id);
        Assert.Equal("upgraded-hash", persisted.PasswordHash);
        Assert.Equal(user.SecurityVersion, persisted.SecurityVersion);
        Assert.Equal(updatedUtc, persisted.UpdatedUtc);

        Assert.False(await context.Store.UpdatePasswordHashAsync(
            user.Id,
            user.PasswordHash!,
            user.SecurityVersion,
            "stale-hash-upgrade",
            updatedUtc.AddMinutes(1)));
        Assert.False(await context.Store.UpdatePasswordHashAsync(
            user.Id,
            "upgraded-hash",
            user.SecurityVersion + 1,
            "stale-version-upgrade",
            updatedUtc.AddMinutes(1)));
    }
}
