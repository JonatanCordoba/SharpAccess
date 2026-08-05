namespace SharpAccess.PackageTests;

public sealed class PullRequestGateStructureTests
{
    [Fact]
    public void ChangedLineCoverageClassifiesOnlyNonExecutableChangesAsNonCoverable()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "changed-line-coverage.ps1"));

        Assert.Contains("Test-NonExecutableChangedText", script, StringComparison.Ordinal);
        Assert.Contains("nonCoverableChangedFiles", script, StringComparison.Ordinal);
        Assert.Contains("missingCoverageFiles", script, StringComparison.Ordinal);
        Assert.Contains(
            "Changed production files with executable or unclassified changes are absent from coverage",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Required packages are absent from coverage",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseCandidateDefaultsAndStatusMatchTheEvidenceContract()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "release-candidate.ps1"));
        string workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "release-candidate.yml"));

        Assert.Contains(
            "[string]$ReferenceEnvironment = \"controlled-windows-runner-01\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "default: controlled-windows-runner-01",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("Assert-ApprovedPerformanceRequest", script, StringComparison.Ordinal);
        Assert.Contains(
            "The approved performance revision is not an ancestor of the selected release-candidate revision.",
            script,
            StringComparison.Ordinal);
        Assert.Contains("-not [bool]$SkipFullLocalGate -and", script, StringComparison.Ordinal);
        Assert.Contains(
            "$evidenceStatus = if ($completeEvidenceRequested) { \"passed\" } else { \"incomplete\" }",
            script,
            StringComparison.Ordinal);
        Assert.Contains("status = \"not-run-by-request\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyReviewKeepsAFailClosedNuGetFallback()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "dependency-review.yml"));

        Assert.Contains("continue-on-error: true", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "steps.dependency_review.outcome == 'failure'",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("NuGetAuditMode=all", workflow, StringComparison.Ordinal);
        Assert.Contains("-warnaserror", workflow, StringComparison.Ordinal);
        Assert.Contains("--locked-mode", workflow, StringComparison.Ordinal);
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
