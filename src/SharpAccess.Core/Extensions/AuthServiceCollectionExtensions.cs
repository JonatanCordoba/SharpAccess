using SharpAccess.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Provides dependency-injection registration for the provider-neutral SharpAccess core.</summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>Registers SharpAccess from the SharpAccess configuration section and optional code overrides.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">A configuration root, the SharpAccess section, or another section containing SharpAccess options.</param>
    /// <param name="configure">Optional code-based overrides applied after configuration binding.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configuration is null.</exception>
    public static IServiceCollection AddSharpAccess(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AuthOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        IConfiguration authConfiguration = ResolveConfigurationSection(configuration, "SharpAccess");
        return services.AddSharpAccess(options =>
        {
            authConfiguration.Bind(options);
            configure?.Invoke(options);
        });
    }

    /// <summary>Registers SharpAccess without replacing the host&apos;s default authentication scheme.</summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configure">The required SharpAccess options configuration.</param>
    /// <returns>The same service collection so additional registrations can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">services or configure is null.</exception>
    public static IServiceCollection AddSharpAccess(
        this IServiceCollection services,
        Action<AuthOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return AuthCoreServiceRegistration.AddSharpAccessCore(services, configure);
    }

    /// <summary>Uses a named child section when a configuration root is provided, or the provided section itself otherwise.</summary>
    private static IConfiguration ResolveConfigurationSection(IConfiguration configuration, string sectionName)
    {
        IConfigurationSection section = configuration.GetSection(sectionName);
        return section.GetChildren().Any() || !string.IsNullOrWhiteSpace(section.Value)
            ? section
            : configuration;
    }
}
