using SharpAccess.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Sqlite")]
public sealed class SqliteServiceRegistrationTests
{
    // Verifies that configuration binding is applied before code overrides.
    [Fact]
    public void AddSqliteAccessConfigurationOverloadBindsThenAppliesCodeOverrides()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpAccess:Sqlite:ConnectionString"] = "Data Source=configured.db"
            })
            .Build();
        ServiceCollection services = new();

        services.AddSqliteAccess(configuration, options =>
        {
            options.ConnectionString = "Data Source=override.db";
        });
        using ServiceProvider provider = services.BuildServiceProvider();

        SqliteAuthOptions options = provider.GetRequiredService<IOptions<SqliteAuthOptions>>().Value;
        Assert.Equal("Data Source=override.db", options.ConnectionString);
    }
}
