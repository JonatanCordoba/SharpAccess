using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpAccess.Postgres;
using Xunit;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresConfigurationSelectionTests
{
    private const string PreferredConnection =
        "Host=localhost;Database=preferred;Username=test;Password=test;Timeout=15;Command Timeout=30;Cancellation Timeout=2000";
    private const string LegacyConnection =
        "Host=localhost;Database=legacy;Username=test;Password=test;Timeout=15;Command Timeout=30;Cancellation Timeout=2000";
    private const string DirectConnection =
        "Host=localhost;Database=direct;Username=test;Password=test;Timeout=15;Command Timeout=30;Cancellation Timeout=2000";

    // Verifies the canonical SharpAccess section wins when both supported roots are present.
    [Fact]
    public void CanonicalSectionTakesPrecedenceOverLegacySection()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["SharpAccess:Postgres:ConnectionString"] = PreferredConnection,
            ["PostgresAccess:ConnectionString"] = LegacyConnection
        });

        PostgresAuthOptions options = ResolveOptions(configuration);

        Assert.Equal(PreferredConnection, options.ConnectionString);
    }

    // Verifies the compatibility PostgresAccess root remains supported when the canonical root is absent.
    [Fact]
    public void LegacySectionIsUsedWhenCanonicalSectionIsAbsent()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["PostgresAccess:ConnectionString"] = LegacyConnection
        });

        PostgresAuthOptions options = ResolveOptions(configuration);

        Assert.Equal(LegacyConnection, options.ConnectionString);
    }

    // Verifies callers may pass a provider-specific section directly instead of a configuration root.
    [Fact]
    public void DirectProviderSectionIsBoundWhenNestedRootsAreAbsent()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Provider:ConnectionString"] = DirectConnection
        });

        PostgresAuthOptions options = ResolveOptions(configuration.GetSection("Provider"));

        Assert.Equal(DirectConnection, options.ConnectionString);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static PostgresAuthOptions ResolveOptions(IConfiguration configuration)
    {
        ServiceCollection services = new();
        services.AddPostgresAccess(configuration);
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        return provider.GetRequiredService<IOptions<PostgresAuthOptions>>().Value;
    }
}
