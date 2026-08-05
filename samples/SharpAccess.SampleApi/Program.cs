using SharpAccess;
using SharpAccess.Attributes;
using SharpAccess.Authorization;
using SharpAccess.SampleApi;
using Microsoft.Extensions.Configuration;

string? requestedEnvironment = Environment.GetEnvironmentVariable("APP_ENV");
if (!string.IsNullOrWhiteSpace(requestedEnvironment))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", requestedEnvironment);
}

if (string.Equals(requestedEnvironment, "Test", StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_RATE_LIMIT_PARTITION_KEY")))
{
    Environment.SetEnvironmentVariable("APP_RATE_LIMIT_PARTITION_KEY", "development-only-rate-limit-partition-key");
}

SampleBootstrapResult bootstrap = SampleLocalSettingsBootstrap.Prepare(args);
WebApplicationBuilder builder = WebApplication.CreateBuilder(bootstrap.HostArguments);
ConfigurationManager configuration = builder.Configuration;
string databaseProvider = configuration["AUTH_DATABASE_PROVIDER"] ?? "sqlite";
if (!string.Equals(databaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("AUTH_DATABASE_PROVIDER must be 'sqlite' for this sample host.");
}

string configuredPort = configuration["APP_PORT"] ?? "5000";
if (!int.TryParse(configuredPort, out int selectedPort) || selectedPort is < 1 or > 65535)
{
    throw new InvalidOperationException("APP_PORT must be an integer from 1 through 65535.");
}

string listenHost = builder.Environment.IsEnvironment("Test") ? "127.0.0.1" : "localhost";
UriBuilder listenAddress = new(Uri.UriSchemeHttp, listenHost, selectedPort);
builder.WebHost.UseUrls(listenAddress.Uri.GetLeftPart(UriPartial.Authority));
SampleDataSeeder.ResetDatabaseIfRequested(builder);
SampleProductionSecurity.ConfigureHostSecurity(builder);
string[] allowedOrigins = SampleCorsConfiguration.Register(builder);
builder.Services.AddOpenApi();
SampleAuthConfiguration.RegisterEmailSender(builder);
SampleAuthConfiguration.RegisterAuth(builder, selectedPort);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<SampleDataSeeder>();
}

WebApplication app = builder.Build();
await app.Services.InitializeSharpAccessAsync(app.Lifetime.ApplicationStopping);
await SampleAuthConfiguration.SeedAdminAsync(app);

SampleProductionSecurity.UseHostSecurity(app);
app.UseDefaultFiles();
app.UseStaticFiles();
if (allowedOrigins.Length > 0)
{
    app.UseCors(SampleCorsConfiguration.PolicyName);
}
app.UseSharpAccessExceptionHandling();
app.UseSharpAccessSecurityHeaders(options =>
{
    options.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
});
app.UseSharpAccess();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/sample/status", SampleConsoleEndpoints.Status).AllowAnonymous();
app.MapAttributedGet("/sample/modules", SampleConsoleEndpoints.Modules);
app.MapGet("/sample/errors/{statusCode:int}", static (int statusCode) =>
        statusCode is >= 400 and <= 599
            ? Results.StatusCode(statusCode)
            : Results.BadRequest(new { message = "statusCode must be between 400 and 599." }))
    .AllowAnonymous();
app.MapSharpAccessEndpoints();
app.MapAttributedGet("/demo/authenticated", DemoHandlers.Authenticated);
app.MapAttributedGet("/demo/admin", DemoHandlers.Admin);
app.MapAttributedGet("/demo/tenant/{tenantId:guid}", DemoHandlers.Tenant);
app.MapFallbackToFile("index.html");

await app.RunAsync();

namespace SharpAccess.SampleApi
{
    internal static class DemoHandlers
    {
        [Authenticate]
        internal static IResult Authenticated() => Results.Ok(new { message = "Authenticated route reached." });

        [RequireGlobalRole(AuthRoles.Admin)]
        [RequireGlobalPermission(AuthPermissions.UsersRead)]
        internal static IResult Admin() => Results.Ok(new { message = "Administrator route reached." });

        [Authenticate]
        [RequireActiveTenant]
        internal static IResult Tenant(Guid tenantId) => Results.Ok(new { tenantId, message = "Tenant route reached." });
    }
}

public partial class Program { }
