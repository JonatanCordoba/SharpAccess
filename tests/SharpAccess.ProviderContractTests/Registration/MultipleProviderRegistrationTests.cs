using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
[Trait("Provider", "Postgres")]
public sealed class MultipleProviderRegistrationTests
{
    [Fact]
    public async Task InitializeFailsWhenMultipleRelationalProvidersAreRegistered()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpAccess:Sqlite:ConnectionString"] = "Data Source=duplicate-provider-test.db"
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSharpAccess(configuration);
        services.AddSqliteAccess(configuration);
        services.AddPostgresAccess(_ =>
            ValueTask.FromException<NpgsqlConnection>(new InvalidOperationException("PostgreSQL connection must not open.")));

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.InitializeSharpAccessAsync());

        Assert.Contains(
            "Only one SharpAccess relational persistence provider can be registered",
            exception.Message,
            StringComparison.Ordinal);
    }
}
