namespace SharpAccess.PackageTests;

public sealed class OperationalReadinessStructureTests
{
    private static readonly string[] RequiredFiles =
    [
        "eng/OperationalReadiness.props",
        "docs/OPERATIONS.md",
        "docs/OBSERVABILITY.md",
        "docs/INCIDENT-RESPONSE.md",
        "docs/BUSINESS-CONTINUITY.md",
        "docs/PRIVACY.md",
        "docs/CHANGE-MANAGEMENT.md",
        "docs/QUALITY-OBJECTIVES.md",
        "docs/RELEASE-CHECKLIST.md",
        "docs/templates/POSTMORTEM.md",
        "docs/templates/RISK-ACCEPTANCE.md",
        "docs/templates/CHANGE-RECORD.md",
        "docs/templates/RECOVERY-DRILL.md",
        "scripts/operational-readiness.ps1",
        "scripts/recovery-drill.ps1",
        ".github/workflows/operational-readiness.yml",
        "src/SharpAccess.Core/Diagnostics/SharpAccessDiagnostics.cs",
        "tests/SharpAccess.UnitTests/DiagnosticsTests.cs",
        "tests/SharpAccess.IntegrationTests/SqliteRecoveryDrillTests.cs"
    ];

    [Fact]
    public void OperationalReadinessControlsExistOnWindows()
    {
        string root = FindRepositoryRoot();
        foreach (string relativePath in RequiredFiles)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Operational readiness file is missing: {relativePath}");
        }

        Assert.False(File.Exists(Path.Combine(root, "scripts", "operational-readiness.sh")));
        Assert.False(File.Exists(Path.Combine(root, "scripts", "recovery-drill.sh")));

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "operational-readiness.yml"));
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("shell: pwsh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ubuntu-latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("macos-latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationalScriptsRequirePowerShellSevenAndStrictMode()
    {
        string root = FindRepositoryRoot();
        foreach (string baseName in new[] { "operational-readiness", "recovery-drill" })
        {
            string scriptPath = Path.Combine(root, "scripts", $"{baseName}.ps1");
            string source = File.ReadAllText(scriptPath);
            Assert.StartsWith("#Requires -Version 7.0", source, StringComparison.Ordinal);
            Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        }
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
