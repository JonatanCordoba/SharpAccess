using System.Data.Common;
using SharpAccess.Persistence;
using SharpAccess.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace SharpAccess.ProviderContractTests;

[Trait("Provider", "Postgres")]
public sealed class PostgresInfrastructureReadinessTests
{
    private const string SafeConnectionString = "Host=localhost;Database=sharpaccess_contract_tests;Username=sharpaccess;Timeout=15;Command Timeout=30;Cancellation Timeout=2000;Minimum Pool Size=0;Maximum Pool Size=20";

    // Verifies connection-string registration creates exactly one provider-owned pooled data source.
    [Fact]
    public async Task ProviderOwnedConnectionSourceUsesOneOwnedDataSource()
    {
        PostgresAuthConnectionFactory factory = new(new PostgresAuthOptions { ConnectionString = SafeConnectionString });
        Assert.True(factory.UsesDataSource);
        Assert.True(factory.OwnsDataSource);
        await factory.DisposeAsync();
        await factory.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.OpenAsync().AsTask());
    }

    // Verifies disposing provider services never disposes a host-owned PostgreSQL data source.
    [Fact]
    public async Task HostOwnedDataSourceRemainsHostOwned()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(SafeConnectionString);
        PostgresAuthConnectionFactory factory = new(dataSource);
        Assert.True(factory.UsesDataSource);
        Assert.False(factory.OwnsDataSource);
        await factory.DisposeAsync();
        await using DbConnection connection = dataSource.CreateConnection();
        Assert.NotNull(connection);
    }

    // Verifies data-source registration preserves host ownership through dependency injection.
    [Fact]
    public async Task DataSourceRegistrationPreservesHostOwnership()
    {
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(SafeConnectionString);
        ServiceCollection services = new();
        services.AddPostgresAccess(dataSource);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        PostgresAuthConnectionFactory factory = Assert.IsType<PostgresAuthConnectionFactory>(provider.GetRequiredService<IPostgresAuthConnectionFactory>());
        Assert.True(factory.UsesDataSource);
        Assert.False(factory.OwnsDataSource);
        Assert.Same(factory, provider.GetRequiredService<PostgresAuthProviderComponents>().Connections);
    }

    // Verifies dependency injection reuses the selected connection factory for all provider components.
    [Fact]
    public async Task RegistrationReusesOneConnectionFactoryAcrossProviderComponents()
    {
        ServiceCollection services = new();
        services.AddPostgresAccess(options => options.ConnectionString = SafeConnectionString);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        IPostgresAuthConnectionFactory connectionFactory = provider.GetRequiredService<IPostgresAuthConnectionFactory>();
        PostgresAuthProviderComponents components = provider.GetRequiredService<PostgresAuthProviderComponents>();
        Assert.Same(connectionFactory, components.Connections);
    }

    // Verifies provider-owned data sources support synchronous service-provider disposal.
    [Fact]
    public void ProviderOwnedDataSourceSupportsSynchronousServiceProviderDisposal()
    {
        ServiceCollection services = new();
        services.AddPostgresAccess(options => options.ConnectionString = SafeConnectionString);
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        _ = provider.GetRequiredService<IPostgresAuthConnectionFactory>();
        provider.Dispose();
    }

    // Verifies safe bounded PostgreSQL defaults satisfy the provider options policy.
    [Fact]
    public void SafeConnectionSettingsPassValidation()
    {
        PostgresAuthOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, new PostgresAuthOptions { ConnectionString = SafeConnectionString });
        Assert.True(result.Succeeded);
    }

    // Verifies absent PostgreSQL configuration fails before connection-string parsing.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionStringFailsClosed(string? connectionString)
    {
        ValidateOptionsResult result = new PostgresAuthOptionsValidator().Validate(
            null,
            new PostgresAuthOptions { ConnectionString = connectionString! });
        Assert.True(result.Failed);
    }

    // Verifies disabling pooling bypasses pool-size policy while preserving all other safety checks.
    [Fact]
    public void DisabledPoolingPassesValidation()
    {
        const string connectionString = "Host=localhost;Database=sharpaccess_contract_tests;Username=sharpaccess;Timeout=15;Command Timeout=30;Cancellation Timeout=2000;Pooling=false";
        ValidateOptionsResult result = new PostgresAuthOptionsValidator().Validate(
            null,
            new PostgresAuthOptions { ConnectionString = connectionString });
        Assert.True(result.Succeeded);
    }

    // Verifies PostgreSQL metadata scalars are interpreted consistently across boolean and numeric results.
    [Theory]
    [InlineData(true, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void MigrationDialectRecognizesTablePresence(object value, bool expected)
    {
        Assert.Equal(expected, new PostgresAuthMigrationDialect().IsTablePresent(value));
    }

    // Verifies unsafe diagnostic, isolation, and unbounded timeout settings fail closed.
    [Theory]
    [InlineData("Include Error Detail=true")]
    [InlineData("Log Parameters=true")]
    [InlineData("Include Failed Batched Command=true")]
    [InlineData("No Reset On Close=true")]
    [InlineData("Multiplexing=true")]
    [InlineData("Timeout=0")]
    [InlineData("Command Timeout=0")]
    [InlineData("Cancellation Timeout=0")]
    [InlineData("Timeout=61")]
    [InlineData("Command Timeout=301")]
    [InlineData("Cancellation Timeout=10001")]
    [InlineData("Maximum Pool Size=501")]
    [InlineData("Minimum Pool Size=21;Maximum Pool Size=20")]
    public void UnsafeConnectionSettingsFailValidation(string setting)
    {
        string connectionString = $"Host=localhost;Database=sharpaccess_contract_tests;Username=sharpaccess;{setting}";
        PostgresAuthOptionsValidator validator = new();
        ValidateOptionsResult result = validator.Validate(null, new PostgresAuthOptions { ConnectionString = connectionString });
        Assert.True(result.Failed);
    }

    // Verifies required parsed connection fields fail without exposing the supplied connection string.
    [Theory]
    [InlineData("Database=sharpaccess_contract_tests;Username=sharpaccess")]
    [InlineData("Host=localhost;Username=sharpaccess")]
    public void MissingConnectionFieldsFailValidation(string connectionString)
    {
        ValidateOptionsResult result = new PostgresAuthOptionsValidator().Validate(null, new PostgresAuthOptions { ConnectionString = connectionString });
        Assert.True(result.Failed);
        Assert.DoesNotContain(connectionString, result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
    }

    // Verifies malformed connection strings fail without echoing their contents.
    [Fact]
    public void MalformedConnectionStringFailsWithoutEchoingValue()
    {
        const string malformed = "Host=localhost;Unknown SharpAccess Option=value";
        ValidateOptionsResult result = new PostgresAuthOptionsValidator().Validate(null, new PostgresAuthOptions { ConnectionString = malformed });
        Assert.True(result.Failed);
        Assert.DoesNotContain(malformed, result.FailureMessage ?? string.Empty, StringComparison.Ordinal);
    }

    // Verifies PostgreSQL operational SQLSTATE values map to the provider-neutral categories.
    [Theory]
    [InlineData("55P03", nameof(AuthDatabaseErrorCategory.Timeout))]
    [InlineData("53300", nameof(AuthDatabaseErrorCategory.ConnectionFailure))]
    [InlineData("57P01", nameof(AuthDatabaseErrorCategory.ConnectionFailure))]
    [InlineData("42P07", nameof(AuthDatabaseErrorCategory.SchemaMismatch))]
    [InlineData("42710", nameof(AuthDatabaseErrorCategory.SchemaMismatch))]
    public void OperationalSqlStatesAreClassified(string sqlState, string expectedCategory)
    {
        Assert.Equal(expectedCategory, PostgresAuthDatabaseErrorClassifier.ClassifySqlState(sqlState).ToString());
    }
}
