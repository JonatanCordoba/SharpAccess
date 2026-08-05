using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Endpoints;
using SharpAccess.Middleware;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Integrates SharpAccess middleware, endpoints, schema initialization, and explicit local seeding.</summary>
public static class AuthApplicationExtensions
{
    /// <summary>Adds the default SharpAccess pipeline without replacing host-wide exception or security-header policies.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccess(this IApplicationBuilder app) =>
        app.UseSharpAccess(static _ => { });

    /// <summary>Adds the package-owned middleware selected by the host.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <param name="configure">A callback that explicitly selects package middleware components.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app or configure is null.</exception>
    public static IApplicationBuilder UseSharpAccess(
        this IApplicationBuilder app,
        Action<SharpAccessMiddlewareOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configure);
        SharpAccessMiddlewareOptions options = new();
        configure(options);

        if (options.InstallExceptionHandler)
        {
            app.UseSharpAccessExceptionHandling();
        }

        if (options.InstallSecurityHeaders)
        {
            app.UseSharpAccessSecurityHeaders();
        }

        if (options.InstallCookieProtection)
        {
            app.UseSharpAccessCookieProtection();
        }

        if (options.InstallRateLimiter)
        {
            app.UseSharpAccessRateLimiter();
        }

        if (options.InstallAuthentication)
        {
            app.UseSharpAccessAuthentication();
        }

        if (options.InstallFreshAuthentication)
        {
            app.UseSharpAccessFreshAuthentication();
        }

        if (options.InstallAuthorization)
        {
            app.UseSharpAccessAuthorization();
        }

        return app;
    }

    /// <summary>Adds the SharpAccess exception boundary only when the host explicitly selects it.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessExceptionHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<AuthExceptionBoundaryMiddleware>();
        app.UseMiddleware<AuthStatusCodePagesMiddleware>();
        return app;
    }

    /// <summary>Adds configurable security headers without selecting a content security policy by default.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <param name="configure">An optional callback that changes the package header values. The host remains responsible for its complete security-header policy.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessSecurityHeaders(
        this IApplicationBuilder app,
        Action<SharpAccessSecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        SharpAccessSecurityHeadersOptions options = new();
        configure?.Invoke(options);
        return app.UseMiddleware<SecurityHeadersMiddleware>(options);
    }

    /// <summary>Adds protection for cookie-backed refresh and logout mutations.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessCookieProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CookieRequestHeaderMiddleware>();
    }

    /// <summary>Adds the SharpAccess endpoint rate limiter.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessRateLimiter(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRateLimiter();
        return app;
    }

    /// <summary>Adds ASP.NET Core authentication for the SharpAccess bearer scheme.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthentication();
        return app;
    }

    /// <summary>Adds recent-authentication enforcement for sensitive mutations.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessFreshAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<FreshAuthenticationMiddleware>();
    }

    /// <summary>Adds ASP.NET Core authorization.</summary>
    /// <param name="app">The application builder to update.</param>
    /// <returns>The same application builder so middleware registration can be chained.</returns>
    /// <exception cref="System.ArgumentNullException">app is null.</exception>
    public static IApplicationBuilder UseSharpAccessAuthorization(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthorization();
        return app;
    }

    /// <summary>Maps every enabled SharpAccess endpoint group.</summary>
    /// <param name="endpoints">The endpoint route builder to update.</param>
    /// <param name="prefix">A bounded local absolute path used as the route-group prefix.</param>
    /// <returns>The mapped SharpAccess route group.</returns>
    /// <exception cref="System.ArgumentNullException">endpoints is null.</exception>
    /// <exception cref="System.ArgumentException">prefix is not a bounded local absolute path.</exception>
    public static RouteGroupBuilder MapSharpAccessEndpoints(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/auth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (string.IsNullOrWhiteSpace(prefix)
            || prefix.Length > 1_024
            || !prefix.StartsWith('/')
            || prefix.StartsWith("//", StringComparison.Ordinal)
            || prefix.Contains('\\')
            || prefix.Contains('?')
            || prefix.Contains('#')
            || prefix.Any(char.IsControl))
        {
            throw new ArgumentException("The endpoint prefix must be a bounded local absolute path.", nameof(prefix));
        }

        string normalizedPrefix = prefix.Length == 1 ? string.Empty : prefix.TrimEnd('/');
        AuthOptions options = endpoints.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        return AuthEndpointMapper.Map(endpoints, normalizedPrefix, options);
    }

    /// <summary>Applies the configured migration mode and validates enabled infrastructure.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token that cancels migration, validation, script generation, or password-hasher initialization.</param>
    /// <returns>A task that represents initialization.</returns>
    /// <remarks>Call this after registering exactly one relational provider. Enabled email workflows also require an IEmailSender implementation.</remarks>
    /// <exception cref="System.ArgumentNullException">services is null.</exception>
    /// <exception cref="System.InvalidOperationException">Provider registration, email delivery, migration configuration, or another required service is invalid.</exception>
    public static async Task InitializeSharpAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        AuthOptions options = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value;
        IEmailSender emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        bool needsEmail = options.Features.Registration || options.Features.PasswordReset;
        if (needsEmail && emailSender is MissingEmailSender)
        {
            throw new InvalidOperationException(
                "Registration or password reset is enabled, but no IEmailSender implementation was registered.");
        }

        ValidateRelationalPersistenceProviderRegistration(scope.ServiceProvider);
        IAuthSchemaManager schemaManager = scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>();
        IHostEnvironment? environment = scope.ServiceProvider.GetService<IHostEnvironment>();
        SharpAccessMigrationMode migrationMode = SharpAccessMigrationModeResolver.Resolve(
            options.Migrations.Mode,
            environment?.EnvironmentName);
        switch (migrationMode)
        {
            case SharpAccessMigrationMode.ApplyAtStartup:
                await schemaManager.MigrateAsync(cancellationToken).ConfigureAwait(false);
                break;
            case SharpAccessMigrationMode.ValidateOnly:
                await schemaManager.ValidateAsync(cancellationToken).ConfigureAwait(false);
                break;
            case SharpAccessMigrationMode.External:
                break;
            case SharpAccessMigrationMode.GenerateScript:
                string script = await schemaManager.GenerateScriptAsync(cancellationToken).ConfigureAwait(false);
                await WriteMigrationScriptAsync(
                    options.Migrations.ScriptOutputPath!,
                    script,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SharpAccess migration mode: {migrationMode}.");
        }

        if (options.Features.PasswordAuthentication)
        {
            IDummyPasswordHashProvider dummyHash = scope.ServiceProvider.GetRequiredService<IDummyPasswordHashProvider>();
            await dummyHash.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Applies all pending provider-owned SharpAccess migrations explicitly.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token that cancels migration.</param>
    /// <returns>A task that represents migration.</returns>
    /// <exception cref="System.ArgumentNullException">services is null.</exception>
    /// <exception cref="System.InvalidOperationException">Exactly one relational provider is not registered or migration validation fails.</exception>
    public static async Task MigrateSharpAccessAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ValidateRelationalPersistenceProviderRegistration(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>()
            .MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates provider-owned SharpAccess schema state without applying DDL or DML.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token that cancels validation.</param>
    /// <returns>A task that represents validation.</returns>
    /// <exception cref="System.ArgumentNullException">services is null.</exception>
    /// <exception cref="System.InvalidOperationException">Exactly one relational provider is not registered or schema state is invalid.</exception>
    public static async Task ValidateSharpAccessSchemaAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ValidateRelationalPersistenceProviderRegistration(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>()
            .ValidateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads provider-neutral SharpAccess migration status without mutating the database.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token that cancels the status query.</param>
    /// <returns>A task whose result describes the current provider-owned schema.</returns>
    /// <exception cref="System.ArgumentNullException">services is null.</exception>
    /// <exception cref="System.InvalidOperationException">Exactly one relational provider is not registered.</exception>
    public static async Task<SharpAccessSchemaStatus> GetSharpAccessSchemaStatusAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ValidateRelationalPersistenceProviderRegistration(scope.ServiceProvider);
        return await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>()
            .GetStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates a provider-native external migration script for the current schema state.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="cancellationToken">A token that cancels script generation.</param>
    /// <returns>A task whose result is the provider-native migration script.</returns>
    /// <exception cref="System.ArgumentNullException">services is null.</exception>
    /// <exception cref="System.InvalidOperationException">Exactly one relational provider is not registered.</exception>
    public static async Task<string> GenerateSharpAccessMigrationScriptAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using IServiceScope scope = services.CreateScope();
        ValidateRelationalPersistenceProviderRegistration(scope.ServiceProvider);
        return await scope.ServiceProvider.GetRequiredService<IAuthSchemaManager>()
            .GenerateScriptAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Seeds one verified administrator only in Development or Test when the host explicitly requests it.</summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="options">The administrator credentials to validate and seed.</param>
    /// <param name="cancellationToken">A token that cancels password hashing or persistence.</param>
    /// <returns>A task that represents the explicit seed operation.</returns>
    /// <remarks>This API is intentionally blocked outside Development and Test environments.</remarks>
    /// <exception cref="System.ArgumentNullException">services or options is null.</exception>
    /// <exception cref="System.ArgumentException">The supplied credentials do not satisfy the configured policy.</exception>
    /// <exception cref="System.InvalidOperationException">The current environment is not Development or Test.</exception>
    public static async Task SeedSharpAccessAdminAsync(
        this IServiceProvider services,
        AdminSeedOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        using IServiceScope scope = services.CreateScope();
        IHostEnvironment environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment()
            && !string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Administrator seeding is allowed only in Development or Test.");
        }

        IInputValidator validator = scope.ServiceProvider.GetRequiredService<IInputValidator>();
        if (!validator.TryValidateEmail(options.Email, out _)
            || !validator.IsValidPassword(options.Password))
        {
            throw new ArgumentException("Administrator seed credentials do not satisfy the configured policy.", nameof(options));
        }

        IPasswordHasher passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        string passwordHash = await passwordHasher.HashAsync(options.Password, cancellationToken).ConfigureAwait(false);
        IAuthAdminSeedStore adminSeedStore = scope.ServiceProvider.GetRequiredService<IAuthAdminSeedStore>();
        IAuthClock clock = scope.ServiceProvider.GetRequiredService<IAuthClock>();
        DateTimeOffset now = clock.UtcNow;
        await adminSeedStore.SeedAdminAsync(
            options,
            passwordHash,
            now,
            SecurityAuditEvidence.Create(
                now,
                "administrator_seeded",
                null,
                null,
                null,
                null,
                "source=explicit_local_seed"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a generated migration script using an atomic same-directory replacement.</summary>
    private static async Task WriteMigrationScriptAsync(
        string outputPath,
        string script,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                script,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>Fails early when no relational provider or multiple relational providers were registered.</summary>
    private static void ValidateRelationalPersistenceProviderRegistration(IServiceProvider services)
    {
        string[] registeredProviders = services
            .GetServices<AuthPrimaryPersistenceProviderRegistration>()
            .Select(provider => provider.Name)
            .ToArray();
        if (registeredProviders.Length == 1)
        {
            return;
        }

        if (registeredProviders.Length == 0)
        {
            throw new InvalidOperationException(
                "Exactly one SharpAccess relational persistence provider must be registered. Add one provider package such as SharpAccess.Sqlite.");
        }

        throw new InvalidOperationException(
            $"Only one SharpAccess relational persistence provider can be registered. Registered providers: {string.Join(", ", registeredProviders)}.");
    }
}
