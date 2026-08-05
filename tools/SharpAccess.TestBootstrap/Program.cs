using SharpAccess;
using SharpAccess.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

BootstrapArguments parsed = BootstrapArguments.Parse(args);
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    EnvironmentName = "Test",
    Args = []
});
builder.Services.AddSingleton<IEmailSender, NullEmailSender>();
builder.Services.AddSharpAccess(options =>
{
    options.BaseUri = new Uri("http://localhost:5000", UriKind.Absolute);
    options.JwtIssuer = "SharpAccess.TestBootstrap";
    options.JwtAudience = "SharpAccess.Tests";
    options.JwtSigningKey = "TEST-ONLY-JWT-SIGNING-KEY-12345678901234567890";
    options.TokenHashing.Key = "TEST-ONLY-TOKEN-HASHING-KEY-123456789012345678";
    options.Passwords.Peppers["v1"] = "TEST-ONLY-PASSWORD-PEPPER-12345678901234567890";
    options.AccessTokenMinutes = 60;
    options.RefreshCookieSecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSqliteAccess(options => options.ConnectionString = $"Data Source={parsed.DatabasePath}");

using IHost host = builder.Build();
await host.Services.InitializeSharpAccessAsync();
await host.Services.SeedSharpAccessAdminAsync(new AdminSeedOptions
{
    Email = parsed.Email,
    Password = parsed.Password
});
Console.WriteLine($"Initialized {Path.GetFullPath(parsed.DatabasePath)} for {parsed.Email}.");

internal sealed record BootstrapArguments(string DatabasePath, string Email, string Password)
{
    // Parses supported bootstrap switches and creates the target directory.
    internal static BootstrapArguments Parse(string[] args)
    {
        string database = Read(args, "--database") ?? "artifacts/test-auth.db";
        string email = Read(args, "--email") ?? "admin@test.local";
        string password = Read(args, "--password") ?? "Admin123!Sample";
        string? directory = Path.GetDirectoryName(Path.GetFullPath(database));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new BootstrapArguments(database, email, password);
    }

    // Reads one named command-line option and rejects a missing value.
    private static string? Read(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => string.Equals(value, name, StringComparison.Ordinal));
        if (index < 0)
        {
            return null;
        }

        if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"A value is required after {name}.");
        }

        return args[index + 1];
    }
}

internal sealed class NullEmailSender : IEmailSender
{
    // Discards email messages in the deterministic test bootstrap host.
    public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
