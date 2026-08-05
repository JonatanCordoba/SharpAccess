using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Persistence;
using SharpAccess.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class AuthApplicationInitializationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("//auth")]
    [InlineData("/auth\\bad")]
    [InlineData("/auth?x=1")]
    [InlineData("/auth#fragment")]
    public void MapSharpAccessEndpointsRejectsInvalidPrefixes(string prefix)
    {
        WebApplication app = CreateApp();

        Assert.Throws<ArgumentException>(() => app.MapSharpAccessEndpoints(prefix));
    }

    [Fact]
    public async Task InitializeSharpAccessRejectsEnabledEmailFeaturesWithoutEmailSender()
    {
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton(Options.Create(new AuthOptions
        {
            Features = new AuthFeatureOptions
            {
                Registration = true
            }
        }));
        services.AddSingleton<IEmailSender, MissingEmailSender>();
        using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeSharpAccessAsync());

        Assert.Contains("no IEmailSender implementation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeSharpAccessRejectsMissingRelationalProviderRegistration()
    {
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton(Options.Create(new AuthOptions()));
        services.AddSingleton<IEmailSender, MissingEmailSender>();
        using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeSharpAccessAsync());

        Assert.Contains("Exactly one SharpAccess relational persistence provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SharpAccess.Sqlite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeSharpAccessRejectsMultipleRelationalProviderRegistrations()
    {
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton(Options.Create(new AuthOptions()));
        services.AddSingleton<IEmailSender, MissingEmailSender>();
        services.AddSingleton(new AuthPrimaryPersistenceProviderRegistration("sqlite"));
        services.AddSingleton(new AuthPrimaryPersistenceProviderRegistration("postgres"));
        using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.InitializeSharpAccessAsync());

        Assert.Contains("Only one SharpAccess relational persistence provider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Registered providers: sqlite, postgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedSharpAccessAdminRejectsNonDevelopmentEnvironments()
    {
        ServiceCollection services = new();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Production"));
        using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.SeedSharpAccessAdminAsync(new AdminSeedOptions
            {
                Email = "admin@example.com",
                Password = "ValidPassword12345"
            }));

        Assert.Contains("Development or Test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeedSharpAccessAdminRejectsCredentialsThatFailPolicy()
    {
        AuthOptions options = TestOptions.Create();
        ServiceCollection services = new();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Test"));
        services.AddSingleton(Options.Create(options));
        services.AddSingleton<IInputValidator, InputValidator>();
        using ServiceProvider provider = services.BuildServiceProvider();

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.SeedSharpAccessAdminAsync(new AdminSeedOptions
            {
                Email = "not-an-email",
                Password = "short"
            }));

        Assert.Contains("configured policy", exception.Message, StringComparison.Ordinal);
    }

    private static WebApplication CreateApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        builder.Services.AddSingleton(Options.Create(new AuthOptions()));
        return builder.Build();
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "SharpAccess.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
