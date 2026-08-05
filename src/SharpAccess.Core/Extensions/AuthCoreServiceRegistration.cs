using SharpAccess;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Endpoints;
using SharpAccess.Middleware;
using SharpAccess.OAuth;
using SharpAccess.Security;
using SharpAccess.Services;
using SharpAccess.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

internal static class AuthCoreServiceRegistration
{
    // Registers provider-neutral authentication services, validation, HTTP clients, and middleware options.
    internal static IServiceCollection AddSharpAccessCore(
        IServiceCollection services,
        Action<AuthOptions> configure)
    {
        services.AddOptions<AuthOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>());
        services.TryAddSingleton<IAuthClock, SystemAuthClock>();
        services.TryAddSingleton<IEmailSender, MissingEmailSender>();
        services.TryAddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.TryAddSingleton<DefaultPasswordRiskValidator>();
        services.TryAddSingleton<BreachedPasswordRiskValidator>();
        services.TryAddSingleton<IPasswordRiskValidator, CompositePasswordRiskValidator>();
        services.TryAddSingleton<IDummyPasswordHashProvider, DummyPasswordHashProvider>();
        services.TryAddSingleton<ITokenProtector, HmacTokenProtector>();
        services.TryAddSingleton<IAuthRateLimitPartitionKeyProvider, AuthRateLimitPartitionKeyProvider>();
        services.TryAddSingleton<IAccessTokenSigningKeyRing, ConfiguredAccessTokenSigningKeyRing>();
        services.TryAddSingleton<IInputValidator, InputValidator>();
        services.TryAddSingleton<IAuthPageCursorCodec, AuthPageCursorCodec>();
        services.TryAddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.TryAddScoped<IAuditService, AuditService>();
        services.TryAddScoped<IAuthSessionIssuer, AuthSessionIssuer>();
        services.TryAddScoped<AuthService>();
        services.TryAddScoped<IRegistrationUseCase, RegistrationUseCase>();
        services.TryAddScoped<IPasswordLoginUseCase, PasswordLoginUseCase>();
        services.TryAddScoped<IRefreshSessionUseCase, RefreshSessionUseCase>();
        services.TryAddScoped<ICurrentUserUseCase, CurrentUserUseCase>();
        services.TryAddScoped<IPasswordChangeUseCase, PasswordChangeUseCase>();
        services.TryAddScoped<IPasswordResetUseCase, PasswordResetUseCase>();
        services.TryAddScoped<IEmailVerificationUseCase, EmailVerificationUseCase>();
        services.TryAddScoped<IAuthService, PasswordRiskAuthService>();
        services.TryAddScoped<IAdministrationService, AdministrationService>();
        services.TryAddScoped<ITenantService, TenantService>();
        services.TryAddScoped<IOAuthService, OAuthService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExternalOAuthProvider, OpenIdConnectOAuthProvider>());

        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddHttpClient(OpenIdConnectOAuthProvider.HttpClientName, static client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpAccess/1.0");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddHttpClient(BreachedPasswordRiskValidator.HttpClientName, static client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpAccess/1.0");
        });
        services.AddProblemDetails();
        services.AddAuthentication()
            .AddJwtBearer(AuthConstants.AuthenticationScheme, static _ => { });
        services.AddOptions<JwtBearerOptions>(AuthConstants.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, IAccessTokenSigningKeyRing, IAuthClock>(
                AuthJwtBearerConfiguration.ConfigureJwtBearer);
        services.AddAuthorization();
        Microsoft.AspNetCore.Builder.RateLimiterServiceCollectionExtensions.AddRateLimiter(
            services,
            AuthRateLimitConfiguration.ConfigureRateLimiter);
        return services;
    }
}
