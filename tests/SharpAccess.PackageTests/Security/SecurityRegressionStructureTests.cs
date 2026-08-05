namespace SharpAccess.PackageTests;

public sealed class SecurityRegressionStructureTests
{
    [Fact]
    public void CrossScopeAttributedAuthorizationBindsTenantAuthorityToRoute()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SharpAccess.Core",
            "Authorization",
            "AttributedEndpointExtensions.cs"));
        string tenantRequirements = SliceBetween(
            source,
            "private static void AddTenantRequirements",
            "private static void AddTenantBindingRequirements");

        Assert.Contains(
            "context.User.HasClaim(AuthConstants.TenantPermissionClaim, requirement.TenantPermission)",
            tenantRequirements,
            StringComparison.Ordinal);
        Assert.Contains(
            "&& TenantMatches(context, requirements.RouteParameter)",
            tenantRequirements,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionBoundaryRequiresExplicitPipelineSelection()
    {
        string repositoryRoot = FindRepositoryRoot();
        string registration = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SharpAccess.Core",
            "Extensions",
            "AuthCoreServiceRegistration.cs"));
        string applicationExtensions = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "SharpAccess.Core",
            "Extensions",
            "AuthApplicationExtensions.cs"));

        Assert.DoesNotContain("AddExceptionHandler<AuthExceptionHandler>", registration, StringComparison.Ordinal);
        Assert.Contains("UseMiddleware<AuthExceptionBoundaryMiddleware>", applicationExtensions, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSourceDoesNotContainRemovedAliasesOrRetiredProviders()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] removed =
        [
            "AddDotNetAuth",
            "UseDotNetAuth",
            "MapDotNetAuthEndpoints",
            "InitializeDotNetAuthAsync",
            "SeedDotNetAuthAdminAsync",
            "AddSqliteAuth",
            "DotNetAuth.",
            "LegacyAuthenticationScheme",
            "LegacyRefreshTokenCookieName",
            "LegacyCsrfHeaderName",
            "AddSqlServerAccess",
            "AddMySqlAccess",
            "SharpAccess.SqlServer",
            "SharpAccess.MySql"
        ];

        IEnumerable<string> productionFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "providers"), "*.cs", SearchOption.AllDirectories));
        foreach (string file in productionFiles)
        {
            string source = File.ReadAllText(file);
            foreach (string removedName in removed)
            {
                Assert.DoesNotContain(removedName, source, StringComparison.Ordinal);
            }
        }
    }

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Required source marker was not found: {startMarker}");
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Required source marker was not found after {startMarker}: {endMarker}");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpAccess.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
