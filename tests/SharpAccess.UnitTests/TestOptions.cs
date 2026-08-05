using SharpAccess.Configuration;
using SharpAccess.Abstractions;

namespace SharpAccess.UnitTests;

internal static class TestOptions
{
    internal static readonly DateTimeOffset Now = new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero);
    internal static IAuthClock Clock { get; } = new FixedClock(Now);
    // Creates a valid baseline configuration for focused option tests.
    internal static AuthOptions Create()
    {
        AuthOptions options = new()
        {
            BaseUri = new Uri("https://app.test"),
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            JwtSigningKey = "TEST-JWT-SIGNING-KEY-12345678901234567890",
            AccessTokenMinutes = 60,
            RequireCsrfHeaderForCookieRefreshRequests = true,
            RefreshTokenCookieName = "__Secure-sharpaccess_refresh"
        };
        options.Features.PasswordAuthentication = true;
        options.Features.Registration = true;
        options.Features.PasswordReset = true;
        options.Features.RefreshTokens = true;
        options.Features.Administration = true;
        options.Features.Tenancy = true;
        options.TokenHashing.Key = "TEST-TOKEN-HASHING-KEY-12345678901234567890";
        options.RateLimits.PartitionKey = "TEST-RATE-LIMIT-PARTITION-KEY-12345678901234567890";
        options.Passwords.Iterations = 1;
        options.Passwords.MemorySizeKiB = 8_192;
        options.Passwords.DegreeOfParallelism = 1;
        options.Passwords.Peppers["v1"] = "TEST-PASSWORD-PEPPER-12345678901234567890";
        return options;
    }

    // Gets the built-in disabled Google-compatible provider entry.
    internal static OpenIdConnectProviderOptions Google(AuthOptions options) =>
        options.OpenIdConnect.Providers["google"];

    // Enables and returns the Google-compatible provider entry.
    internal static OpenIdConnectProviderOptions EnableGoogle(AuthOptions options)
    {
        OpenIdConnectProviderOptions google = Google(options);
        google.Enabled = true;
        return google;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
