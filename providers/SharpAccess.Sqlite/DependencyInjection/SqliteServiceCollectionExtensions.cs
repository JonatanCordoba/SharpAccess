using SharpAccess.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers SQLite persistence for SharpAccess.</summary>
public static class SqliteServiceCollectionExtensions
{
    /// <summary>Registers the SQLite provider from configuration and optional code overrides.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">A configuration root, SharpAccess:Sqlite section, or provider section.</param>
    /// <param name="configure">Optional code-based overrides applied after configuration binding.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configuration is null.</exception>
    public static IServiceCollection AddSqliteAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SharpAccess.Sqlite.SqliteAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfiguration sqliteConfiguration = ResolveSqliteConfiguration(configuration);
        return services.AddSqliteAccess(options =>
        {
            sqliteConfiguration.Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers SQLite persistence with host-managed logical connection creation.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="connectionFactory">A factory that returns a new usable logical connection for each operation. The provider disposes returned connections.</param>
    /// <param name="configure">Optional provider options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <remarks>The factory must honor cancellation and must not return a concurrently shared mutable connection.</remarks>
    /// <exception cref="System.ArgumentNullException">services or connectionFactory is null.</exception>
    public static IServiceCollection AddSqliteAccess(
        this IServiceCollection services,
        Func<CancellationToken, ValueTask<SqliteConnection>> connectionFactory,
        Action<SharpAccess.Sqlite.SqliteAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        IServiceCollection result = services.AddSqliteAccess(options =>
        {
            configure?.Invoke(options);
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                options.ConnectionString = "Data Source=:memory:";
            }
        });
        services.Replace(ServiceDescriptor.Singleton<SharpAccess.Sqlite.ISqliteAuthConnectionFactory>(
            new SharpAccess.Sqlite.SqliteAuthConnectionFactory(connectionFactory)));
        return result;
    }

    /// <summary>Registers the SQLite provider and its ordered migration engine.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">The required SQLite provider options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configure is null.</exception>
    public static IServiceCollection AddSqliteAccess(
        this IServiceCollection services,
        Action<SharpAccess.Sqlite.SqliteAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        SQLitePCL.Batteries_V2.Init();
        services.AddOptions<SharpAccess.Sqlite.SqliteAuthOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<SharpAccess.Sqlite.SqliteAuthOptions>,
            SharpAccess.Sqlite.SqliteAuthOptionsValidator>());
        services.AddSingleton(new AuthPrimaryPersistenceProviderRegistration("sqlite"));
        services.TryAddSingleton<SharpAccess.Sqlite.ISqliteAuthConnectionFactory>(static provider =>
            new SharpAccess.Sqlite.SqliteAuthConnectionFactory(
                provider.GetRequiredService<IOptions<SharpAccess.Sqlite.SqliteAuthOptions>>()));
        services.TryAddSingleton<IAuthConnectionFactory>(static provider =>
            provider.GetRequiredService<SharpAccess.Sqlite.ISqliteAuthConnectionFactory>() as IAuthConnectionFactory
            ?? throw new InvalidOperationException("The SQLite connection factory contract is unavailable."));
        services.TryAddSingleton<IAuthSqlDialect, SharpAccess.Sqlite.SqliteAuthSqlDialect>();
        services.TryAddSingleton<IAuthCommandFactory, SharpAccess.Sqlite.SqliteAuthCommandFactory>();
        services.TryAddSingleton<IAuthMigrationProvider, SharpAccess.Sqlite.SqliteAuthMigrationProvider>();
        services.TryAddSingleton<IAuthTransactionManager, SharpAccess.Sqlite.SqliteAuthTransactionManager>();
        services.TryAddSingleton<IAuthDatabaseErrorClassifier, SharpAccess.Sqlite.SqliteAuthDatabaseErrorClassifier>();
        services.TryAddSingleton<IAuthDatabaseProvider, SharpAccess.Sqlite.SqliteAuthDatabaseProvider>();
        services.AddSharpAccessAuthStore<SharpAccess.Sqlite.SqliteAuthStore>();
        return services;
    }

    /// <summary>Uses SharpAccess:Sqlite when a configuration root is provided, or the provided section itself otherwise.</summary>
    private static IConfiguration ResolveSqliteConfiguration(IConfiguration configuration)
    {
        IConfigurationSection sharpAccessSqlite = configuration.GetSection("SharpAccess:Sqlite");
        return sharpAccessSqlite.GetChildren().Any() || !string.IsNullOrWhiteSpace(sharpAccessSqlite.Value)
            ? sharpAccessSqlite
            : configuration;
    }
}
