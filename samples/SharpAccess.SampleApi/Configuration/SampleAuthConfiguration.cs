using SharpAccess;
using SharpAccess.Configuration;
using Microsoft.Extensions.Configuration;

namespace SharpAccess.SampleApi;

internal static class SampleAuthConfiguration
{
    // Registers the local capturing mailbox or validates and registers production SMTP delivery.
    public static void RegisterEmailSender(WebApplicationBuilder builder)
    {
        ConfigurationManager configuration = builder.Configuration;
        if (builder.Environment.IsDevelopment()
            || string.Equals(builder.Environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<SampleMailbox>();
            builder.Services.AddSingleton<IEmailSender>(static services => services.GetRequiredService<SampleMailbox>());
            return;
        }

        if (!configuration.GetValue<bool>("Auth:Features:Registration")
            && !configuration.GetValue<bool>("Auth:Features:PasswordReset"))
        {
            return;
        }

        string smtpHost = configuration["SMTP_HOST"]
            ?? throw new InvalidOperationException("SMTP_HOST is required outside Development and Test.");
        string smtpPortValue = configuration["SMTP_PORT"]
            ?? throw new InvalidOperationException("SMTP_PORT is required outside Development and Test.");
        if (!int.TryParse(smtpPortValue, out int smtpPort) || smtpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("SMTP_PORT must be an integer from 1 through 65535.");
        }

        string smtpUsername = configuration["SMTP_USERNAME"]
            ?? throw new InvalidOperationException("SMTP_USERNAME is required outside Development and Test.");
        string smtpPassword = configuration["SMTP_PASSWORD"]
            ?? throw new InvalidOperationException("SMTP_PASSWORD is required outside Development and Test.");
        string smtpFromEmail = configuration["SMTP_FROM_EMAIL"]
            ?? throw new InvalidOperationException("SMTP_FROM_EMAIL is required outside Development and Test.");
        builder.Services.AddSingleton<IEmailSender>(
            new SmtpEmailSender(smtpHost, smtpPort, smtpUsername, smtpPassword, smtpFromEmail));
    }

    // Maps host configuration into SharpAccess, including the generic Google-compatible OIDC entry.
    public static void RegisterAuth(WebApplicationBuilder builder, int selectedPort)
    {
        ConfigurationManager configuration = builder.Configuration;
        builder.Services.AddSharpAccess(options =>
        {
            options.BaseUri = new Uri(configuration["APP_BASE_URL"] ?? configuration["Auth:BaseUri"] ?? $"http://localhost:{selectedPort}", UriKind.Absolute);
            if (options.BaseUri.Port != selectedPort)
            {
                throw new InvalidOperationException("APP_BASE_URL must use the same port as APP_PORT.");
            }

            options.JwtIssuer = configuration["APP_JWT_ISSUER"] ?? configuration["Auth:Issuer"] ?? "SharpAccess.SampleApi";
            options.JwtAudience = configuration["APP_JWT_AUDIENCE"] ?? configuration["Auth:Audience"] ?? "SharpAccess.SampleClients";
            options.JwtSigningKey = configuration["APP_JWT_KEY"] ?? configuration["Auth:SigningKey"] ?? string.Empty;
            options.TokenHashing.Key = configuration["APP_REFRESH_TOKEN_HASH_KEY"] ?? configuration["Auth:TokenHashingKey"] ?? string.Empty;
            options.Passwords.CurrentPepperVersion = configuration["APP_PASSWORD_PEPPER_VERSION"] ?? configuration["Auth:PasswordPepperVersion"] ?? "v1";
            string? configuredPepper = configuration["APP_PASSWORD_PEPPER"] ?? configuration["Auth:PasswordPepper"];
            if (!string.IsNullOrWhiteSpace(configuredPepper))
            {
                options.Passwords.Peppers[options.Passwords.CurrentPepperVersion] = configuredPepper;
            }

            options.AccessTokenMinutes = configuration.GetValue<int?>("APP_JWT_ACCESS_TOKEN_MINUTES")
                ?? (builder.Environment.IsProduction() ? 15 : 60);
            options.RefreshTokenDays = configuration.GetValue<int?>("APP_REFRESH_TOKEN_DAYS")
                ?? configuration.GetValue<int?>("Auth:RefreshTokenDays")
                ?? options.RefreshTokenDays;
            options.EmailVerificationMinutes = configuration.GetValue<int?>("Auth:EmailVerificationMinutes") ?? options.EmailVerificationMinutes;
            options.PasswordResetMinutes = configuration.GetValue<int?>("Auth:PasswordResetMinutes") ?? options.PasswordResetMinutes;
            options.Lockout.FailedAttempts = configuration.GetValue<int?>("APP_LOCKOUT_FAILED_ATTEMPTS")
                ?? configuration.GetValue<int?>("Auth:Lockout:FailedAttempts")
                ?? options.Lockout.FailedAttempts;
            options.Lockout.Minutes = configuration.GetValue<int?>("APP_LOCKOUT_MINUTES")
                ?? configuration.GetValue<int?>("Auth:Lockout:Minutes")
                ?? options.Lockout.Minutes;
            options.RateLimits.LoginPerMinute = configuration.GetValue<int?>("APP_RATE_LIMIT_LOGIN_PER_MINUTE")
                ?? configuration.GetValue<int?>("Auth:RateLimits:LoginPerMinute")
                ?? options.RateLimits.LoginPerMinute;
            options.RateLimits.RegisterPerMinute = configuration.GetValue<int?>("Auth:RateLimits:RegisterPerMinute") ?? options.RateLimits.RegisterPerMinute;
            options.RateLimits.RefreshPerMinute = configuration.GetValue<int?>("Auth:RateLimits:RefreshPerMinute") ?? options.RateLimits.RefreshPerMinute;
            options.RateLimits.PasswordResetPerMinute = configuration.GetValue<int?>("Auth:RateLimits:PasswordResetPerMinute") ?? options.RateLimits.PasswordResetPerMinute;
            options.RateLimits.EmailVerificationPerMinute = configuration.GetValue<int?>("Auth:RateLimits:EmailVerificationPerMinute") ?? options.RateLimits.EmailVerificationPerMinute;
            options.RateLimits.OAuthPerMinute = configuration.GetValue<int?>("Auth:RateLimits:OAuthPerMinute") ?? options.RateLimits.OAuthPerMinute;
            options.RateLimits.PartitionKey = configuration["APP_RATE_LIMIT_PARTITION_KEY"]
                ?? configuration["Auth:RateLimits:PartitionKey"]
                ?? string.Empty;
            options.RefreshCookieSecurePolicy = builder.Environment.IsProduction()
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            SampleProductionSecurity.ApplyAuthSecurity(options, configuration, builder.Environment);
            options.ReturnRefreshTokenInResponseBody = configuration.GetValue<bool>("Auth:ReturnRefreshTokenInResponseBody");
            options.Features.PasswordAuthentication = configuration.GetValue<bool>("Auth:Features:PasswordAuthentication");
            options.Features.Registration = configuration.GetValue<bool>("Auth:Features:Registration");
            options.Features.PasswordReset = configuration.GetValue<bool>("Auth:Features:PasswordReset");
            options.Features.RefreshTokens = configuration.GetValue<bool>("Auth:Features:RefreshTokens");
            options.Features.Administration = configuration.GetValue<bool>("Auth:Features:Administration");
            options.Features.Tenancy = configuration.GetValue<bool>("Auth:Features:Tenancy");
            OpenIdConnectProviderOptions google = options.OpenIdConnect.Providers["google"];
            google.Enabled = configuration.GetValue<bool?>("OAUTH_GOOGLE_ENABLED")
                ?? configuration.GetValue<bool>("Auth:OpenIdConnect:Providers:google:Enabled");
            google.ClientId = configuration["OAUTH_GOOGLE_CLIENT_ID"]
                ?? configuration["Auth:OpenIdConnect:Providers:google:ClientId"]
                ?? string.Empty;
            google.ClientSecret = configuration["OAUTH_GOOGLE_CLIENT_SECRET"]
                ?? configuration["Auth:OpenIdConnect:Providers:google:ClientSecret"]
                ?? string.Empty;
        });

        builder.Services.AddSqliteAccess(options =>
        {
            options.ConnectionString = configuration["AUTH_CONNECTION_STRING"]
                ?? configuration.GetConnectionString("Auth")
                ?? "Data Source=App_Data/sample-auth.db";
        });
    }

    // Seeds the explicitly configured sample administrator during startup.
    public static async Task SeedAdminAsync(WebApplication app)
    {
        IConfiguration configuration = app.Configuration;
        if (!(configuration.GetValue<bool?>("APP_SEED_ADMIN") ?? configuration.GetValue<bool>("Auth:SeedTestAdmin")))
        {
            return;
        }

        await app.Services.SeedSharpAccessAdminAsync(
            new AdminSeedOptions
            {
                Email = configuration["APP_SEED_ADMIN_EMAIL"] ?? configuration["Auth:TestAdminEmail"] ?? "admin@test.local",
                Password = configuration["APP_SEED_ADMIN_PASSWORD"] ?? configuration["Auth:TestAdminPassword"]
                    ?? throw new InvalidOperationException("APP_SEED_ADMIN_PASSWORD is required when seeding is enabled.")
            },
            app.Lifetime.ApplicationStopping).ConfigureAwait(false);
    }
}
