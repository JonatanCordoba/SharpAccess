using System.Collections.Concurrent;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace SharpAccess.IntegrationTests;

public sealed class AuthFlowIntegrationTests : IAsyncLifetime
{
    private string _databasePath = null!;
    private ServiceProvider _provider = null!;
    private CapturingEmailSender _emails = null!;

    // Verifies that initialize asynchronously.
    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sharpaccess-{Guid.NewGuid():N}.db");
        _emails = new CapturingEmailSender();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IEmailSender>(_emails);
        services.AddSharpAccess(options => Configure(options));
        services.AddSqliteAccess(options => options.ConnectionString = $"Data Source={_databasePath};Pooling=False");
        _provider = services.BuildServiceProvider(validateScopes: true);
        await _provider.InitializeSharpAccessAsync();
    }

    // Verifies that dispose asynchronously.
    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }

    // Verifies that registration requires verification before login.
    [Fact]
    public async Task RegistrationRequiresVerificationBeforeLogin()
    {
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService service = scope.ServiceProvider.GetRequiredService<IAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "integration-test");

        ServiceResult<string> registered = await service.RegisterAsync(
            "new.user@example.com",
            "ValidPassword123",
            metadata);
        Assert.True(registered.Succeeded);
        Assert.Single(_emails.Messages);

        ServiceResult<SessionTokens> beforeVerification = await service.LoginAsync(
            "new.user@example.com",
            "ValidPassword123",
            null,
            metadata);
        Assert.False(beforeVerification.Succeeded);

        string token = ExtractFragmentToken(_emails.Messages.Single().TextBody, "verify_token");
        Assert.True((await service.VerifyEmailAsync(token, metadata)).Succeeded);
        ServiceResult<SessionTokens> login = await service.LoginAsync(
            "new.user@example.com",
            "ValidPassword123",
            null,
            metadata);
        Assert.True(login.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(login.Value?.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.Value?.RefreshToken));
        JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(login.Value!.AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(jwt.Kid));
    }

    // Verifies that refresh reuse revokes the replacement family.
    [Fact]
    public async Task RefreshReuseRevokesTheReplacementFamily()
    {
        SessionTokens session = await CreateVerifiedSessionAsync("rotate@example.com");
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService service = scope.ServiceProvider.GetRequiredService<IAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "integration-test");

        ServiceResult<SessionTokens> rotated = await service.RefreshAsync(session.RefreshToken, null, metadata);
        Assert.True(rotated.Succeeded);
        Assert.NotEqual(session.RefreshToken, rotated.Value!.RefreshToken);

        ServiceResult<SessionTokens> reused = await service.RefreshAsync(session.RefreshToken, null, metadata);
        Assert.False(reused.Succeeded);

        ServiceResult<SessionTokens> replacementAfterReuse = await service.RefreshAsync(
            rotated.Value.RefreshToken,
            null,
            metadata);
        Assert.False(replacementAfterReuse.Succeeded);
    }

    // Verifies that password reset invalidates existing sessions.
    [Fact]
    public async Task PasswordResetInvalidatesExistingSessions()
    {
        SessionTokens session = await CreateVerifiedSessionAsync("reset@example.com");
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService service = scope.ServiceProvider.GetRequiredService<IAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "integration-test");

        _emails.Messages.Clear();
        Assert.True((await service.ForgotPasswordAsync("reset@example.com", metadata)).Succeeded);
        string resetToken = ExtractFragmentToken(_emails.Messages.Single().TextBody, "reset_token");
        Assert.True((await service.ResetPasswordAsync(resetToken, "ReplacementPassword456", metadata)).Succeeded);
        Assert.False((await service.RefreshAsync(session.RefreshToken, null, metadata)).Succeeded);
        Assert.False((await service.LoginAsync("reset@example.com", "ValidPassword123", null, metadata)).Succeeded);
        Assert.True((await service.LoginAsync("reset@example.com", "ReplacementPassword456", null, metadata)).Succeeded);
    }

    // Verifies that lockout is applied after configured failures.
    [Fact]
    public async Task LockoutIsAppliedAfterConfiguredFailures()
    {
        await CreateVerifiedSessionAsync("lockout@example.com");
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService service = scope.ServiceProvider.GetRequiredService<IAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "integration-test");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.False((await service.LoginAsync("lockout@example.com", "WrongPassword123", null, metadata)).Succeeded);
        }

        Assert.False((await service.LoginAsync("lockout@example.com", "ValidPassword123", null, metadata)).Succeeded);
    }

    // Verifies that create verified session asynchronously.
    private async Task<SessionTokens> CreateVerifiedSessionAsync(string email)
    {
        _emails.Messages.Clear();
        await using AsyncServiceScope scope = _provider.CreateAsyncScope();
        IAuthService service = scope.ServiceProvider.GetRequiredService<IAuthService>();
        RequestMetadata metadata = new("127.0.0.1", "integration-test");
        Assert.True((await service.RegisterAsync(email, "ValidPassword123", metadata)).Succeeded);
        string token = ExtractFragmentToken(_emails.Messages.Single().TextBody, "verify_token");
        Assert.True((await service.VerifyEmailAsync(token, metadata)).Succeeded);
        ServiceResult<SessionTokens> login = await service.LoginAsync(email, "ValidPassword123", null, metadata);
        Assert.True(login.Succeeded);
        return login.Value!;
    }

    // Verifies that configure.
    private static void Configure(AuthOptions options)
    {
        options.BaseUri = new Uri("https://app.test");
        options.JwtIssuer = "integration-tests";
        options.JwtAudience = "integration-clients";
        options.JwtSigningKey = "INTEGRATION-JWT-SIGNING-KEY-123456789012345678";
        options.Features.PasswordAuthentication = true;
        options.Features.Registration = true;
        options.Features.PasswordReset = true;
        options.Features.RefreshTokens = true;
        options.Features.Administration = true;
        options.Features.Tenancy = true;
        options.TokenHashing.Key = "INTEGRATION-TOKEN-HASH-KEY-123456789012345678";
        options.RateLimits.PartitionKey = "INTEGRATION-RATE-LIMIT-KEY-12345678901234567890";
        options.Passwords.Iterations = 1;
        options.Passwords.MemorySizeKiB = 8_192;
        options.Passwords.DegreeOfParallelism = 1;
        options.Passwords.Peppers["v1"] = "INTEGRATION-PASSWORD-PEPPER-123456789012345";
        options.Lockout.FailedAttempts = 3;
        options.RefreshCookieSecurePolicy = CookieSecurePolicy.Always;
        options.RefreshTokenCookieName = "__Secure-sharpaccess_refresh";
        options.RequireCsrfHeaderForCookieRefreshRequests = true;
        options.Migrations.Mode = SharpAccessMigrationMode.ApplyAtStartup;
    }

    // Verifies that extract fragment token.
    private static string ExtractFragmentToken(string text, string name)
    {
        int marker = text.IndexOf($"#{name}=", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        string encoded = text[(marker + name.Length + 2)..].Trim();
        return Uri.UnescapeDataString(encoded);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        internal ConcurrentBag<AuthEmailMessage> Messages { get; } = [];

        // Verifies that send asynchronously.
        public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
