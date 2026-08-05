using Microsoft.Extensions.DependencyInjection;
using SharpAccess;
using SharpAccess.Configuration;
using SharpAccess.Persistence;
using SharpAccess.Postgres;
using Xunit;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresAuthorizationCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    // Verifies the internal authorization reader handles both global and tenant parameter shapes.
    [Trait("Capability", "GlobalAuthorizationContract")]
    [Trait("Capability", "TenantAuthorizationContract")]
    [PostgresFact]
    public async Task AuthorizationReaderHandlesGlobalRowsAndTenantScope()
    {
        string connectionString = PostgresProviderContractTestSupport.RequireConnectionString();
        await PostgresProviderContractTestSupport.ResetDatabaseAsync(connectionString).ConfigureAwait(false);
        await using ServiceProvider provider = PostgresProviderContractTestSupport.CreateProvider(connectionString);
        using IServiceScope scope = provider.CreateScope();
        IAuthStore store = scope.ServiceProvider.GetRequiredService<IAuthStore>();
        await store.InitializeAsync().ConfigureAwait(false);

        AdminSeedOptions options = new()
        {
            Email = $"authorization-reader-{Guid.NewGuid():N}@example.com",
            Password = "unused-by-store"
        };
        await store.SeedAdminAsync(options, "authorization-reader-hash", Now).ConfigureAwait(false);
        SharpAccess.Domain.AuthUser user = (await store.FindUserByNormalizedEmailAsync(
            options.Email.ToUpperInvariant()).ConfigureAwait(false))!;

        PostgresAuthProviderComponents components = PostgresAuthProviderFactory.Create(connectionString);
        IReadOnlyList<string> globalRoles = await components.AuthorizationStore.GetGlobalRolesAsync(user.Id).ConfigureAwait(false);
        IReadOnlyList<string> globalPermissions = await components.AuthorizationStore.GetGlobalPermissionsAsync(user.Id).ConfigureAwait(false);
        IReadOnlyList<string> tenantRoles = await components.AuthorizationStore.GetTenantRolesAsync(
            user.Id,
            Guid.NewGuid()).ConfigureAwait(false);
        IReadOnlyList<string> tenantPermissions = await components.AuthorizationStore.GetTenantPermissionsAsync(
            user.Id,
            Guid.NewGuid()).ConfigureAwait(false);

        Assert.Contains(AuthRoles.Admin, globalRoles);
        Assert.NotEmpty(globalPermissions);
        Assert.Empty(tenantRoles);
        Assert.Empty(tenantPermissions);
    }
}
