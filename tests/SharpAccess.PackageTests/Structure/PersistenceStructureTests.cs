namespace SharpAccess.PackageTests;

public sealed class PersistenceStructureTests
{
    [Fact]
    public void CoreServicesUseResponsibilitySpecificPersistenceContracts()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = ReadSources(Path.Combine(repositoryRoot, "src", "SharpAccess.Core"));

        Assert.DoesNotContain("IAuthStore store", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IAuthStore>", source, StringComparison.Ordinal);
        Assert.Contains("IAuthSessionStore", source, StringComparison.Ordinal);
        Assert.Contains("IAuthAdministrationStore", source, StringComparison.Ordinal);
        Assert.Contains("IAuthTenantManagementStore", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AggregateAuthStoreIsCompositionOnly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string persistenceRoot = Path.Combine(repositoryRoot, "src", "SharpAccess.Core", "Persistence");
        string aggregate = File.ReadAllText(Path.Combine(persistenceRoot, "IAuthStore.cs"));

        Assert.DoesNotContain("Task<", aggregate, StringComparison.Ordinal);
        Assert.DoesNotContain("Task ", aggregate, StringComparison.Ordinal);
        Assert.Contains("IAuthUserStore", aggregate, StringComparison.Ordinal);
        Assert.Contains("IAuthAuthorizationStore", aggregate, StringComparison.Ordinal);
        Assert.Contains("IAuthTenantStore", aggregate, StringComparison.Ordinal);
        Assert.Contains("IAuthAuditStore", aggregate, StringComparison.Ordinal);
        foreach (string ledger in new[]
        {
            "IAuthUserStore.cs",
            "IAuthTokenStore.cs",
            "IAuthOAuthStore.cs",
            "IAuthAuthorizationStore.cs",
            "IAuthTenantStore.cs",
            "IAuthAuditStore.cs",
            "IAuthStoreCapabilities.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(persistenceRoot, ledger)), $"Missing persistence capability ledger: {ledger}");
        }
    }

    [Fact]
    public void ActiveProviderPaginationSqlIsBoundedAndOffsetFree()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string provider in new[] { "Sqlite", "Postgres" })
        {
            string source = ReadSources(Path.Combine(repositoryRoot, "providers", $"SharpAccess.{provider}", "Stores"));

            Assert.DoesNotContain("OFFSET @offset", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@afterCreated IS NULL", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("$afterCreated IS NULL", source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AuthPageSupport.GetFetchLimit", source, StringComparison.Ordinal);
            Assert.Contains("AuthPageSupport.CreateSlice", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ActiveProvidersExposeEquivalentPersistenceInfrastructure()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string provider in new[] { "Sqlite", "Postgres" })
        {
            string source = ReadSources(Path.Combine(repositoryRoot, "providers", $"SharpAccess.{provider}"));

            Assert.Contains("IAuthConnectionFactory", source, StringComparison.Ordinal);
            Assert.Contains("IAuthCommandFactory", source, StringComparison.Ordinal);
            Assert.Contains("IAuthSqlDialect", source, StringComparison.Ordinal);
            Assert.Contains("IAuthMigrationProvider", source, StringComparison.Ordinal);
            Assert.Contains("IAuthTransactionManager", source, StringComparison.Ordinal);
            Assert.Contains("IAuthDatabaseProvider", source, StringComparison.Ordinal);
            Assert.Contains("IAuthDatabaseErrorClassifier", source, StringComparison.Ordinal);
            Assert.Contains("Func<CancellationToken, ValueTask<", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PersistenceOwnershipAndTransactionContractIsDocumented()
    {
        string repositoryRoot = FindRepositoryRoot();
        string document = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "PERSISTENCE-AND-CONNECTIONS.md"));

        foreach (string boundary in new[]
        {
            "Account creation",
            "One-time-token replacement",
            "Password change",
            "Refresh rotation/reuse",
            "Tenant creation",
            "Ownership transfer",
            "Role/permission assignment",
            "User deactivation",
            "Migration"
        })
        {
            Assert.Contains(boundary, document, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(File.Exists(Path.Combine(repositoryRoot, "samples", "SharedDatabase", "README.md")));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, "samples", "DedicatedAuthenticationDatabase", "README.md")));
    }

    private static string ReadSources(string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpAccess.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SharpAccess repository root.");
    }
}
