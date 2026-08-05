namespace SharpAccess.PackageTests;

public sealed class PostgresParityStructureTests
{
    [Fact]
    public void PostgresIsSupportedPublicAndWindowsEvidenceBacked()
    {
        string root = FindRepositoryRoot();
        string[] requiredFiles =
        [
            "docs/POSTGRES-OPERATIONS.md",
            "docs/POSTGRES-PROMOTION.md",
            "scripts/postgres-recovery-drill.ps1",
            "scripts/postgres-promotion.ps1",
            "tests/SharpAccess.ProviderContractTests/Infrastructure/PostgresInfrastructureReadinessTests.cs",
            "tests/SharpAccess.ProviderContractTests/Migrations/PostgresOperationalContractTests.cs"
        ];
        Assert.All(
            requiredFiles,
            relative => Assert.True(File.Exists(Path.Combine(root, relative)), $"Missing PostgreSQL evidence file: {relative}"));

        string project = File.ReadAllText(Path.Combine(root, "providers", "SharpAccess.Postgres", "SharpAccess.Postgres.csproj"));
        string publicApi = File.ReadAllText(Path.Combine(root, "eng", "public-api", "SharpAccess.Postgres.txt"));
        string connectionFactory = File.ReadAllText(Path.Combine(root, "providers", "SharpAccess.Postgres", "Persistence", "Connections", "PostgresAuthConnectionFactory.cs"));
        string registration = File.ReadAllText(Path.Combine(root, "providers", "SharpAccess.Postgres", "DependencyInjection", "PostgresServiceCollectionExtensions.cs"));
        string validator = File.ReadAllText(Path.Combine(root, "providers", "SharpAccess.Postgres", "Configuration", "PostgresAuthOptionsValidator.cs"));
        string migrationDialect = File.ReadAllText(Path.Combine(root, "providers", "SharpAccess.Postgres", "Persistence", "Schema", "PostgresAuthMigrationDialect.cs"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "provider-contracts.yml"));
        string verifyLocal = File.ReadAllText(Path.Combine(root, "scripts", "verify-local.ps1"));
        string recovery = File.ReadAllText(Path.Combine(root, "scripts", "postgres-recovery-drill.ps1"));
        string promotion = File.ReadAllText(Path.Combine(root, "scripts", "postgres-promotion.ps1"));
        string operations = File.ReadAllText(Path.Combine(root, "docs", "POSTGRES-OPERATIONS.md"));

        Assert.Contains("Supported PostgreSQL persistence provider", project, StringComparison.Ordinal);
        Assert.Contains("SharpAccessPostgresStatus", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.PostgresServiceCollectionExtensions", publicApi, StringComparison.Ordinal);
        Assert.Contains("NpgsqlDataSource", connectionFactory, StringComparison.Ordinal);
        Assert.Contains("IAsyncDisposable", connectionFactory, StringComparison.Ordinal);
        Assert.Contains("_ownsDataSource", connectionFactory, StringComparison.Ordinal);
        Assert.Contains("public static class PostgresServiceCollectionExtensions", registration, StringComparison.Ordinal);
        Assert.Contains("CreateWithConnections", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgresAuthProviderFactory.Create(options.ConnectionString)", registration, StringComparison.Ordinal);
        foreach (string prohibitedSetting in new[] { "IncludeErrorDetail", "LogParameters", "IncludeFailedBatchedCommand", "NoResetOnClose", "Multiplexing" })
        {
            Assert.Contains(prohibitedSetting, validator, StringComparison.Ordinal);
        }
        Assert.Contains("pg_try_advisory_xact_lock", migrationDialect, StringComparison.Ordinal);
        Assert.Contains("lock_timeout", migrationDialect, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("SHARPACCESS_POSTGRES_READINESS", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/postgres-recovery-drill.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("services:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("docker", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("postgres-recovery-drill", verifyLocal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHARPACCESS_PROVIDER_TEST_ALLOW_RESET", recovery, StringComparison.Ordinal);
        Assert.Contains("sharpaccess_contract_tests_", recovery, StringComparison.Ordinal);
        Assert.Contains("pg_dump", recovery, StringComparison.Ordinal);
        Assert.Contains("pg_restore", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerId", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("docker", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provider-coverage.ps1", promotion, StringComparison.Ordinal);
        Assert.Contains("mutation-test.ps1", promotion, StringComparison.Ordinal);
        Assert.Contains("postgres-recovery-drill.ps1", promotion, StringComparison.Ordinal);
        Assert.Contains("verify-local.ps1", promotion, StringComparison.Ordinal);
        Assert.Contains("Supported", operations, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpAccess.sln"))) { return current.FullName; }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the SharpAccess repository root.");
    }
}