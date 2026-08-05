using SharpAccess.Persistence;
using SharpAccess.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers PostgreSQL persistence for SharpAccess.</summary>
public static class PostgresServiceCollectionExtensions
{
    private const string HostManagedConnectionStringMarker =
        "Host=localhost;Database=sharpaccess_host_managed;Username=sharpaccess;Timeout=15;Command Timeout=30;Cancellation Timeout=2000";

    /// <summary>Registers PostgreSQL persistence from configuration and optional code overrides.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">A configuration root, SharpAccess:Postgres section, PostgresAccess section, or provider section.</param>
    /// <param name="configure">Optional code-based overrides applied after configuration binding.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configuration is null.</exception>
    public static IServiceCollection AddPostgresAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PostgresAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        IConfiguration postgresConfiguration = ResolvePostgresConfiguration(configuration);
        return services.AddPostgresAccess(options =>
        {
            postgresConfiguration.Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers PostgreSQL persistence from a host-owned data source without transferring ownership.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="dataSource">The host-owned data source used to create logical connections. SharpAccess does not dispose it.</param>
    /// <param name="configure">Optional provider options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or dataSource is null.</exception>
    public static IServiceCollection AddPostgresAccess(
        this IServiceCollection services,
        NpgsqlDataSource dataSource,
        Action<PostgresAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dataSource);
        return AddPostgresAccessWithFactory(
            services,
            new PostgresAuthConnectionFactory(dataSource),
            configure);
    }

    /// <summary>Registers PostgreSQL persistence with host-managed logical connection creation.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="connectionFactory">A factory that returns a new usable logical connection for each operation. The provider disposes returned connections.</param>
    /// <param name="configure">Optional provider options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <remarks>The factory must honor cancellation and must not return a concurrently shared mutable connection.</remarks>
    /// <exception cref="System.ArgumentNullException">services or connectionFactory is null.</exception>
    public static IServiceCollection AddPostgresAccess(
        this IServiceCollection services,
        Func<CancellationToken, ValueTask<NpgsqlConnection>> connectionFactory,
        Action<PostgresAuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        return AddPostgresAccessWithFactory(
            services,
            new PostgresAuthConnectionFactory(connectionFactory),
            configure);
    }

    /// <summary>Registers PostgreSQL persistence, migrations, and the complete auth store.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">The required PostgreSQL provider options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configure is null.</exception>
    public static IServiceCollection AddPostgresAccess(
        this IServiceCollection services,
        Action<PostgresAuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<PostgresAuthOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<PostgresAuthOptions>,
            PostgresAuthOptionsValidator>());
        services.AddSingleton(new AuthPrimaryPersistenceProviderRegistration("postgres"));
        services.TryAddSingleton<IPostgresAuthConnectionFactory>(static provider =>
            new PostgresAuthConnectionFactory(
                provider.GetRequiredService<IOptions<PostgresAuthOptions>>().Value));
        services.TryAddSingleton<IAuthConnectionFactory>(static provider =>
            provider.GetRequiredService<IPostgresAuthConnectionFactory>() as IAuthConnectionFactory
            ?? throw new InvalidOperationException("The PostgreSQL connection factory contract is unavailable."));
        services.TryAddSingleton<IAuthSqlDialect, PostgresAuthSqlDialect>();
        services.TryAddSingleton<IAuthCommandFactory, PostgresAuthCommandFactory>();
        services.TryAddSingleton<IAuthMigrationProvider, PostgresAuthMigrationProvider>();
        services.TryAddSingleton<IAuthTransactionManager, PostgresAuthTransactionManager>();
        services.TryAddSingleton<IAuthDatabaseErrorClassifier, PostgresAuthDatabaseErrorClassifier>();
        services.TryAddSingleton<IAuthDatabaseProvider, PostgresAuthDatabaseProvider>();
        services.TryAddSingleton(static provider =>
            PostgresAuthProviderFactory.CreateWithConnections(
                provider.GetRequiredService<IPostgresAuthConnectionFactory>()));
        services.AddSharpAccessAuthStore<PostgresAuthStore>();
        return services;
    }

    /// <summary>Replaces the default provider-owned source with one host-managed connection source.</summary>
    private static IServiceCollection AddPostgresAccessWithFactory(
        IServiceCollection services,
        IPostgresAuthConnectionFactory connectionFactory,
        Action<PostgresAuthOptions>? configure)
    {
        IServiceCollection result = services.AddPostgresAccess(options =>
        {
            configure?.Invoke(options);
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                options.ConnectionString = HostManagedConnectionStringMarker;
            }
        });
        services.Replace(ServiceDescriptor.Singleton(connectionFactory));
        services.Replace(ServiceDescriptor.Singleton<PostgresAuthProviderComponents>(static provider =>
            PostgresAuthProviderFactory.CreateWithConnections(
                provider.GetRequiredService<IPostgresAuthConnectionFactory>())));
        return result;
    }

    /// <summary>Uses SharpAccess:Postgres or PostgresAccess when a configuration root is provided, or the provided section itself otherwise.</summary>
    private static IConfiguration ResolvePostgresConfiguration(IConfiguration configuration)
    {
        IConfigurationSection sharpAccessPostgres = configuration.GetSection("SharpAccess:Postgres");
        if (HasConfigurationValue(sharpAccessPostgres))
        {
            return sharpAccessPostgres;
        }

        IConfigurationSection postgresAccess = configuration.GetSection("PostgresAccess");
        return HasConfigurationValue(postgresAccess) ? postgresAccess : configuration;
    }

    private static bool HasConfigurationValue(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section.Value) || section.GetChildren().Any();
}
