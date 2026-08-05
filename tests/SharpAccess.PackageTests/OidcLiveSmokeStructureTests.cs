namespace SharpAccess.PackageTests;

public sealed class OidcLiveSmokeStructureTests
{
    private static readonly string[] RequiredFiles =
    [
        ".github/workflows/release-candidate.yml",
        "docs/OIDC-LIVE-SMOKE.md",
        "scripts/oidc-live-smoke.ps1",
        "tests/SharpAccess.IntegrationTests/OidcEmulatorIntegrationTests.cs",
        "tests/SharpAccess.IntegrationTests/OidcLiveSmokeTests.cs"
    ];

    [Fact]
    public void OidcValidationControlsExistInTheWindowsReleasePath()
    {
        string root = FindRepositoryRoot();
        foreach (string relativePath in RequiredFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))),
                $"OIDC validation control is missing: {relativePath}");
        }

        Assert.False(File.Exists(Path.Combine(root, "scripts", "oidc-live-smoke.sh")));
        Assert.False(File.Exists(Path.Combine(root, ".github", "workflows", "oidc-live-smoke.yml")));
    }

    [Fact]
    public void IntegratedWorkflowProtectsLiveOidcEvidence()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-candidate.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: release-candidate", workflow, StringComparison.Ordinal);
        Assert.Contains("RequireOidcLiveEvidence", workflow, StringComparison.Ordinal);
        Assert.Contains("SHARPACCESS_OIDC_LIVE_CLIENT_SECRET", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(".trx", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("screenshots", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveScriptUsesBoundedRedactedEvidence()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "oidc-live-smoke.ps1"));

        Assert.Contains("Category=OidcLive", script, StringComparison.Ordinal);
        Assert.Contains("redacted-no-credentials-codes-tokens-nonce-account-data-or-endpoints", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--diag", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveScriptNormalizesMissingSettingsForStrictMode()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "oidc-live-smoke.ps1"));

        Assert.Contains("$missing = @(", script, StringComparison.Ordinal);
        Assert.Contains("$required | Where-Object", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$missing = $required | Where-Object", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDiagnosticsExposeOnlyBoundedRequestStages()
    {
        string root = FindRepositoryRoot();
        string liveTest = File.ReadAllText(
            Path.Combine(root, "tests", "SharpAccess.IntegrationTests", "OidcLiveSmokeTests.cs"));

        Assert.Contains("without exposing provider payloads", liveTest, StringComparison.Ordinal);
        Assert.Contains("token_requests=", liveTest, StringComparison.Ordinal);
        Assert.Contains("jwks_requests=", liveTest, StringComparison.Ordinal);
        Assert.Contains(
            "catch (ExternalOAuthProviderException exception)",
            liveTest,
            StringComparison.Ordinal);
        Assert.Contains(
            "clients.DescribeFailure(),",
            liveTest,
            StringComparison.Ordinal);
        Assert.Contains(
            "exception);",
            liveTest,
            StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", liveTest, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.ToString", liveTest, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsStringAsync", liveTest, StringComparison.Ordinal);
        Assert.DoesNotContain("response.Content", liveTest, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegratedReleaseCandidateConsumesFreshOidcEvidenceBeforeLongRunningStages()
    {
        string root = FindRepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "scripts", "release-candidate.ps1"));

        int oidc = script.IndexOf(
            "Invoke-ReleaseCandidateStage \"Protected OIDC live smoke\"",
            StringComparison.Ordinal);
        int localGate = script.IndexOf(
            "Invoke-ReleaseCandidateStage \"Complete Windows clean-tree local gate\"",
            StringComparison.Ordinal);
        int performance = script.IndexOf(
            "Invoke-ReleaseCandidateStage \"Performance and capacity evidence\"",
            StringComparison.Ordinal);
        int postgres = script.IndexOf(
            "Invoke-ReleaseCandidateStage \"PostgreSQL real-engine provider contracts\"",
            StringComparison.Ordinal);

        Assert.True(oidc >= 0);
        Assert.True(localGate >= 0);
        Assert.True(performance >= 0);
        Assert.True(postgres >= 0);
        Assert.True(oidc < localGate);
        Assert.True(oidc < performance);
        Assert.True(oidc < postgres);

        string oidcBlock = script[oidc..localGate];
        Assert.DoesNotContain("NoRestore", oidcBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("NoBuild", oidcBlock, StringComparison.Ordinal);
        Assert.Contains("finally {", oidcBlock, StringComparison.Ordinal);
        Assert.Contains("Clear-EphemeralOidcEnvironment", oidcBlock, StringComparison.Ordinal);

        Assert.Contains("SHARPACCESS_OIDC_LIVE_AUTHORIZATION_CODE", script, StringComparison.Ordinal);
        Assert.Contains("SHARPACCESS_OIDC_LIVE_CODE_VERIFIER", script, StringComparison.Ordinal);
        Assert.Contains("SHARPACCESS_OIDC_LIVE_NONCE", script, StringComparison.Ordinal);
        Assert.Contains(
            "Remove-Item -LiteralPath \"Env:$name\"",
            script,
            StringComparison.Ordinal);
    }

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

        throw new InvalidOperationException("Could not locate the SharpAccess repository root.");
    }
}
