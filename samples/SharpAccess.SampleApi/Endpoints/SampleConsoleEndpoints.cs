using SharpAccess.Attributes;
using SharpAccess.Configuration;
using Microsoft.Extensions.Options;

namespace SharpAccess.SampleApi;

internal static class SampleConsoleEndpoints
{
    internal static IResult Status(
        IConfiguration configuration,
        IHostEnvironment environment,
        IOptions<AuthOptions> authOptions)
    {
        AuthOptions options = authOptions.Value;
        string[] providers = options.OpenIdConnect.Providers
            .Where(static pair => pair.Value?.Enabled == true)
            .Select(static pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return Results.Ok(new
        {
            environment = environment.EnvironmentName,
            configured = true,
            setupStorage = "Windows Credential Manager",
            resetSetupCommand = "dotnet run --project samples/SharpAccess.SampleApi -- --reset-local-setup",
            resetDataCommand = "dotnet run --project samples/SharpAccess.SampleApi -- --reset-sample-data",
            accounts = new
            {
                administrator = configuration["APP_SEED_ADMIN_EMAIL"] ?? "admin@test.local",
                manager = configuration["SAMPLE_MANAGER_EMAIL"] ?? "manager@test.local",
                user = configuration["SAMPLE_USER_EMAIL"] ?? "user@test.local"
            },
            features = new
            {
                passwordAuthentication = options.Features.PasswordAuthentication,
                registration = options.Features.Registration,
                passwordReset = options.Features.PasswordReset,
                refreshTokens = options.Features.RefreshTokens,
                administration = options.Features.Administration,
                tenancy = options.Features.Tenancy
            },
            providers,
            frontend = new
            {
                framework = "none",
                design = "Material Design-inspired local HTML, CSS, and JavaScript"
            }
        });
    }

    [Authenticate]
    internal static IResult Modules(HttpContext context)
    {
        HashSet<string> permissions = context.User.FindAll(AuthConstants.GlobalPermissionClaim)
            .Select(static claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        return Results.Ok(new
        {
            items = SampleModuleCatalog.All.Select(module => new
            {
                module.Id,
                module.DisplayName,
                module.Description,
                module.RoleName,
                module.PermissionName,
                module.Icon,
                granted = permissions.Contains(module.PermissionName)
            })
        });
    }
}
