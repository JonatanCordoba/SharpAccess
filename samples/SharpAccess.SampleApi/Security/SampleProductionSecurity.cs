using System.Net;
using System.Security.Cryptography.X509Certificates;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace SharpAccess.SampleApi;

internal static class SampleProductionSecurity
{
    public static void ConfigureHostSecurity(WebApplicationBuilder builder)
    {
        ConfigurationManager configuration = builder.Configuration;
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedHost
                | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = configuration.GetValue<int?>("APP_FORWARDED_HEADERS_LIMIT") ?? 1;
            foreach (string proxy in SplitConfigurationList(configuration["APP_FORWARDED_HEADERS_KNOWN_PROXIES"]))
            {
                if (!IPAddress.TryParse(proxy, out IPAddress? address))
                {
                    throw new InvalidOperationException("APP_FORWARDED_HEADERS_KNOWN_PROXIES must contain only IP addresses.");
                }

                options.KnownProxies.Add(address);
            }
        });

        if (!builder.Environment.IsProduction())
        {
            return;
        }

        string keysDirectory = configuration["APP_DATA_PROTECTION_KEYS_DIRECTORY"]
            ?? throw new InvalidOperationException("APP_DATA_PROTECTION_KEYS_DIRECTORY is required in Production.");
        string certificatePath = configuration["APP_DATA_PROTECTION_CERTIFICATE_PATH"]
            ?? throw new InvalidOperationException("APP_DATA_PROTECTION_CERTIFICATE_PATH is required in Production.");
        string? certificatePassword = configuration["APP_DATA_PROTECTION_CERTIFICATE_PASSWORD"];
        Directory.CreateDirectory(keysDirectory);
        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
        builder.Services.AddDataProtection()
            .SetApplicationName(configuration["APP_DATA_PROTECTION_APP_NAME"] ?? "SharpAccess.SampleApi")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
            .ProtectKeysWithCertificate(certificate);
    }

    public static void ApplyAuthSecurity(AuthOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        options.FreshAuthenticationMinutes = configuration.GetValue<int?>("APP_FRESH_AUTHENTICATION_MINUTES")
            ?? options.FreshAuthenticationMinutes;
        options.RequireCsrfHeaderForCookieRefreshRequests = configuration.GetValue<bool?>("APP_REQUIRE_COOKIE_CONFIRMATION_HEADER")
            ?? configuration.GetValue<bool?>("Auth:RequireCsrfHeaderForCookieRefreshRequests")
            ?? environment.IsProduction();
        options.CsrfHeaderName = configuration["APP_COOKIE_CONFIRMATION_HEADER_NAME"]
            ?? configuration["Auth:CsrfHeaderName"]
            ?? options.CsrfHeaderName;
        options.CsrfHeaderValue = configuration["APP_COOKIE_CONFIRMATION_HEADER_VALUE"]
            ?? configuration["Auth:CsrfHeaderValue"]
            ?? options.CsrfHeaderValue;
        if (environment.IsProduction()
            && !options.RefreshTokenCookieName.StartsWith("__Secure-", StringComparison.Ordinal))
        {
            options.RefreshTokenCookieName = "__Secure-" + options.RefreshTokenCookieName;
        }
    }

    public static void UseHostSecurity(WebApplication app)
    {
        app.UseForwardedHeaders();
        if (app.Environment.IsProduction())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }
    }

    private static string[] SplitConfigurationList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
