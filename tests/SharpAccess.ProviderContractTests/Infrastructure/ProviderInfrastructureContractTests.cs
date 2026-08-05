using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharpAccess.Persistence;
using SharpAccess.Postgres;
using SharpAccess.Sqlite;

namespace SharpAccess.ProviderContractTests;

public sealed class ProviderInfrastructureContractTests
{
    [Fact]
    [Trait("Provider", "Sqlite")]
    public async Task SqliteHostConnectionFactoryCreatesInitializedLogicalConnections()
    {
        int calls = 0;
        SqliteAuthConnectionFactory factory = new(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls++;
            return ValueTask.FromResult(new SqliteConnection("Data Source=:memory:"));
        });

        await using SqliteConnection first = await factory.OpenAsync();
        await using SqliteConnection second = await factory.OpenAsync();

        Assert.Equal(2, calls);
        Assert.NotSame(first, second);
        Assert.Equal(ConnectionState.Open, first.State);
        Assert.Equal(ConnectionState.Open, second.State);
        Assert.Equal(1, await ReadPragmaIntAsync(first, "foreign_keys"));
        Assert.Equal(5000, await ReadPragmaIntAsync(first, "busy_timeout"));
    }

    [Fact]
    [Trait("Provider", "Postgres")]
    public async Task PostgresUsesSelectedHostConnectionFactory()
    {
        InvalidOperationException expected = new("postgres-host-factory");
        ServiceCollection services = new();
        services.AddPostgresAccess(_ => ValueTask.FromException<NpgsqlConnection>(expected));
        await using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IPostgresAuthConnectionFactory>().OpenAsync().AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    [Trait("Provider", "Postgres")]
    public void PostgresDataSourceRegistrationIsPubliclyAvailable()
    {
        Assert.True(typeof(PostgresServiceCollectionExtensions).IsPublic);
        Assert.Contains(
            typeof(PostgresServiceCollectionExtensions).GetMethods(),
            method => method.Name == "AddPostgresAccess"
                && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(NpgsqlDataSource)));
    }

    [Fact]
    [Trait("Provider", "Sqlite")]
    [Trait("Provider", "Postgres")]
    [Trait("MutationInvariant", "PostgresErrorClassification")]
    public void ActiveProvidersClassifyDatabaseFailuresConsistently()
    {
        Assert.Equal(
            AuthDatabaseErrorCategory.UniqueConstraint,
            SqliteAuthDatabaseErrorClassifier.ClassifyCodes(19, 2067));
        Assert.Equal(
            AuthDatabaseErrorCategory.ForeignKeyConstraint,
            SqliteAuthDatabaseErrorClassifier.ClassifyCodes(19, 787));
        Assert.Equal(
            AuthDatabaseErrorCategory.SerializationFailure,
            PostgresAuthDatabaseErrorClassifier.ClassifySqlState("40001"));
        Assert.Equal(
            AuthDatabaseErrorCategory.Deadlock,
            PostgresAuthDatabaseErrorClassifier.ClassifySqlState("40P01"));
    }

    [Fact]
    [Trait("Provider", "Sqlite")]
    [Trait("Provider", "Postgres")]
    public void ActiveProviderMigrationCatalogsEndWithPaginationIndexes()
    {
        IAuthMigrationProvider[] providers =
        [
            new SqliteAuthMigrationProvider(),
            new PostgresAuthMigrationProvider()
        ];

        Assert.All(providers, provider => Assert.Equal("012_pagination_indexes", provider.GetMigrations()[^1].Id));
    }

    [Fact]
    [Trait("Provider", "Sqlite")]
    public void SqliteRegistrationExposesResponsibilitySpecificStoresAndClassifier()
    {
        ServiceCollection services = new();
        services.AddSqliteAccess(options => options.ConnectionString = "Data Source=:memory:");
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IAuthDatabase database = scope.ServiceProvider.GetRequiredService<IAuthDatabase>();
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthSessionStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthUserTenantStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthUserOneTimeTokenStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthRefreshSessionStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthOAuthPersistenceStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthAdministrationStore>());
        Assert.Same(database, scope.ServiceProvider.GetRequiredService<IAuthTenantManagementStore>());
        Assert.IsType<SqliteAuthDatabaseErrorClassifier>(
            scope.ServiceProvider.GetRequiredService<IAuthDatabaseErrorClassifier>());
    }

    private static async Task<int> ReadPragmaIntAsync(SqliteConnection connection, string name)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = FormattableString.Invariant($"PRAGMA {name};");
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}