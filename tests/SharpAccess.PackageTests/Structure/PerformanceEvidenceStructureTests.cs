namespace SharpAccess.PackageTests;

public sealed class PerformanceEvidenceStructureTests
{
    // Prevents the controlled baseline from regressing to the incomplete SQLite-only profile.
    [Fact]
    public void ControlledPerformanceEvidenceOwnsRequiredScopeAndApproval()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "performance-evidence.ps1"));
        string policy = File.ReadAllText(Path.Combine(root, "eng", "ReleaseCandidate.props"));
        string unitEvidence = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "SharpAccess.UnitTests",
            "Performance",
            "ReleaseCandidateCryptographyPerformanceEvidenceTests.cs"));
        string endpointEvidence = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "SharpAccess.EndpointTests",
            "Performance",
            "ReleaseCandidateEndpointPerformanceEvidenceTests.cs"));
        string postgresEvidence = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "SharpAccess.ProviderContractTests",
            "Performance",
            "ReleaseCandidatePostgresPerformanceEvidenceTests.cs"));

        string[] requiredMetrics =
        [
            "password_hash_queue_saturation",
            "password_hash_no_wait_rejection",
            "endpoint_refresh_replay_contention",
            "postgres_user_keyset_page",
            "postgres_tenant_member_keyset_page"
        ];
        Assert.All(requiredMetrics, metric =>
            Assert.Contains(metric, script, StringComparison.Ordinal));
        Assert.Contains("password_hash_queue_saturation", unitEvidence, StringComparison.Ordinal);
        Assert.Contains("password_hash_no_wait_rejection", unitEvidence, StringComparison.Ordinal);
        Assert.Contains("endpoint_refresh_replay_contention", endpointEvidence, StringComparison.Ordinal);
        Assert.Contains("postgres_user_keyset_page", postgresEvidence, StringComparison.Ordinal);
        Assert.Contains("postgres_tenant_member_keyset_page", postgresEvidence, StringComparison.Ordinal);

        Assert.Contains("postgresql.json", script, StringComparison.Ordinal);
        Assert.Contains("PerformanceWarmupIterations", script, StringComparison.Ordinal);
        Assert.Contains("PerformancePostgresUserRows", script, StringComparison.Ordinal);
        Assert.Contains("PerformancePostgresTenantMemberRows", script, StringComparison.Ordinal);
        Assert.Contains("environmentFingerprint", script, StringComparison.Ordinal);
        Assert.Contains("ApproveBaseline", script, StringComparison.Ordinal);
        Assert.Contains("ReviewDecision", script, StringComparison.Ordinal);
        Assert.Contains("p95ComparisonEpsilonMilliseconds", script, StringComparison.Ordinal);
        Assert.Contains("PerformanceIndependentRuns", script, StringComparison.Ordinal);
        Assert.Contains("median-across-independent-processes", script, StringComparison.Ordinal);
        Assert.Contains("independentRunP95Milliseconds", script, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-DevSkimSafeJsonString", script, StringComparison.Ordinal);
        Assert.Contains(
            "existing reviewed candidate without rerunning measurements",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Assert-ApprovedRevisionScope", script, StringComparison.Ordinal);
        Assert.Contains("Assert-NoSensitiveRetainedEvidence", script, StringComparison.Ordinal);

        Assert.Contains(
            "<PerformanceP95ComparisonEpsilonMilliseconds>",
            policy,
            StringComparison.Ordinal);
        Assert.Contains("<PerformanceIndependentRuns>", policy, StringComparison.Ordinal);
        Assert.Contains("<PerformanceWarmupIterations>", policy, StringComparison.Ordinal);
        Assert.Contains("<PerformancePostgresUserRows>", policy, StringComparison.Ordinal);
        Assert.Contains("<PerformancePostgresTenantMemberRows>", policy, StringComparison.Ordinal);
    }

    // Finds the repository root from the built test assembly.
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
