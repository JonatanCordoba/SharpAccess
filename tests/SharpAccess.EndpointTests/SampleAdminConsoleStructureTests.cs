namespace SharpAccess.EndpointTests;

public sealed class SampleAdminConsoleStructureTests
{
    [Fact]
    public void SampleRemainsNonPackableAndSourceReferenced()
    {
        string project = Read("samples/SharpAccess.SampleApi/SharpAccess.SampleApi.csproj");
        Assert.Contains("<IsPackable>false</IsPackable>", project, StringComparison.Ordinal);
        Assert.Contains("../../src/SharpAccess.Core/SharpAccess.Core.csproj", project, StringComparison.Ordinal);
        Assert.Contains("../../providers/SharpAccess.Sqlite/SharpAccess.Sqlite.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<PackageId>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstRunSetupUsesWindowsCredentialManagerInsteadOfTrackedSettings()
    {
        string bootstrap = Read("samples/SharpAccess.SampleApi/Configuration/SampleLocalSettings.cs");
        Assert.Contains("Windows Credential Manager", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CredWriteW", bootstrap, StringComparison.Ordinal);
        Assert.Contains("CredReadW", bootstrap, StringComparison.Ordinal);
        Assert.Contains("--reset-local-setup", bootstrap, StringComparison.Ordinal);
        Assert.Contains("HasValidAccountPasswords", bootstrap, StringComparison.Ordinal);
        Assert.Contains("value.Any(char.IsLetter)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("value.Any(char.IsDigit)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("15 to 256 characters", bootstrap, StringComparison.Ordinal);
        Assert.Contains("email.Trim().Split('@', 2)[0]", bootstrap, StringComparison.Ordinal);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", bootstrap, StringComparison.Ordinal);
        Assert.Contains("must not contain the email name before @", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ReadAccountPassword(\"Administrator password\", adminEmail)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ReadAccountPassword(\"Tenant manager password\", managerEmail)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("ReadAccountPassword(\"Standard user password\", userEmail)", bootstrap, StringComparison.Ordinal);

        string appSettings = Read("samples/SharpAccess.SampleApi/appsettings.json");
        Assert.DoesNotContain("APP_JWT_KEY", appSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("APP_PASSWORD_PEPPER", appSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("APP_REFRESH_TOKEN_HASH_KEY", appSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleSeedsThreeUsersAndPermissionBackedModules()
    {
        string seeder = Read("samples/SharpAccess.SampleApi/Data/SampleDataSeeder.cs");
        Assert.Contains("APP_SEED_ADMIN_EMAIL", seeder, StringComparison.Ordinal);
        Assert.Contains("SAMPLE_MANAGER_EMAIL", seeder, StringComparison.Ordinal);
        Assert.Contains("SAMPLE_USER_EMAIL", seeder, StringComparison.Ordinal);
        Assert.Contains("/auth/register", seeder, StringComparison.Ordinal);
        Assert.Contains("/admin/users/", seeder, StringComparison.Ordinal);
        Assert.Contains("/tenants/", seeder, StringComparison.Ordinal);

        string modules = Read("samples/SharpAccess.SampleApi/Modules/SampleModule.cs");
        Assert.Contains("internal sealed record SampleModule", modules, StringComparison.Ordinal);
        Assert.Contains("AuthPermissions.UsersRead", modules, StringComparison.Ordinal);
        Assert.Contains("AuthPermissions.TenantsRead", modules, StringComparison.Ordinal);
        Assert.Contains("AuthPermissions.AuditRead", modules, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserConsoleIsFrameworklessAndContainsRequiredSections()
    {
        string html = Read("samples/SharpAccess.SampleApi/wwwroot/index.html");
        string javascript = Read("samples/SharpAccess.SampleApi/wwwroot/js/app.js");
        string oauthFlow = Read("samples/SharpAccess.SampleApi/wwwroot/js/oauth-flow.js");
        string combined = html + javascript + oauthFlow;

        foreach (string section in new[] { "Users", "Tenants", "Roles", "Permissions", "Modules", "Audit", "Sample settings" })
        {
            Assert.Contains(section, combined, StringComparison.Ordinal);
        }

        Assert.Contains("/auth/login", javascript, StringComparison.Ordinal);
        Assert.Contains("/admin/users", javascript, StringComparison.Ordinal);
        Assert.Contains("/sample/modules", javascript, StringComparison.Ordinal);
        Assert.Contains("renderError(404", javascript, StringComparison.Ordinal);
        Assert.Contains("renderError(500", javascript, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/js/oauth-flow.js\" defer></script>", html, StringComparison.Ordinal);

        foreach (string forbidden in new[] { "react", "angular", "vue", "svelte", "jquery", "bootstrap.min", "tailwind", "unpkg.com", "cdn.jsdelivr.net" })
        {
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BrowserOpenIdConnectFlowUsesLocalReturnAndOneTimeExchange()
    {
        string oauthFlow = Read("samples/SharpAccess.SampleApi/wwwroot/js/oauth-flow.js");
        Assert.Contains("localReturnUrl = `/?oauth_provider=${safeProvider}`", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("/challenge?returnUrl=", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("fragment.get('oauth_code')", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("history.replaceState({}, '', '/')", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("/exchange", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify({ code, tenantId: null })", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("event.stopImmediatePropagation()", oauthFlow, StringComparison.Ordinal);
        Assert.Contains("toMessage(payload.errors)", oauthFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("location.origin", oauthFlow, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpAccess.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("SharpAccess.sln was not found above the endpoint-test output directory.");
    }
}
