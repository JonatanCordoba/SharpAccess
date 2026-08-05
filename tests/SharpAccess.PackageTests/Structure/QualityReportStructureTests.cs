using System.Xml.Linq;

namespace SharpAccess.PackageTests;

public sealed class QualityReportStructureTests
{
    [Fact]
    public void QualityReportIsRevisionBoundOfflineCompleteAndFinal()
    {
        string root = FindRepositoryRoot();
        string policyPath = Path.Combine(root, "eng", "QualityReportPolicy.props");
        string scriptPath = Path.Combine(root, "scripts", "quality-report.ps1");
        string postgresCoveragePath = Path.Combine(root, "scripts", "postgres-quality-coverage.ps1");
        string localCiPath = Path.Combine(root, "scripts", "local-ci.ps1");
        string packPath = Path.Combine(root, "scripts", "pack.ps1");
        string releasePath = Path.Combine(root, "scripts", "release-dry-run.ps1");
        string projectPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "SharpAccess.QualityReport.csproj");
        string entryPointPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportEntryPoint.cs");
        string programPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "Program.cs");
        string processorPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.cs");
        string processorInfrastructurePath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.Infrastructure.cs");
        string hotspotPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.Hotspots.cs");
        string metricsPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.Metrics.cs");
        string htmlPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.Html.cs");
        string policyOwnershipPath = Path.Combine(root, "tools", "SharpAccess.QualityReport", "QualityReportPostProcessor.Policy.cs");

        Assert.True(File.Exists(policyPath));
        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(postgresCoveragePath));
        Assert.True(File.Exists(localCiPath));
        Assert.True(File.Exists(packPath));
        Assert.True(File.Exists(projectPath));
        Assert.True(File.Exists(entryPointPath));
        Assert.True(File.Exists(programPath));
        Assert.True(File.Exists(processorPath));
        Assert.True(File.Exists(processorInfrastructurePath));
        Assert.True(File.Exists(hotspotPath));
        Assert.True(File.Exists(metricsPath));
        Assert.True(File.Exists(htmlPath));
        Assert.True(File.Exists(policyOwnershipPath));

        XDocument policy = XDocument.Load(policyPath);
        string[] assemblies = policy.Descendants("QualityReportProject")
            .Select(element => element.Attribute("Assembly")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["SharpAccess.Core", "SharpAccess.Postgres", "SharpAccess.Sqlite"], assemblies);
        Assert.Equal("2", policy.Descendants("QualityReportSchemaVersion").Single().Value);
        Assert.Equal("EvidenceOnly", policy.Descendants("QualityReportEnforcement").Single().Value);

        string script = File.ReadAllText(scriptPath);
        Assert.Contains("artifacts/quality-report/index.html", script, StringComparison.Ordinal);
        Assert.Contains("manifest revision differs from checked-out HEAD", script, StringComparison.Ordinal);
        Assert.Contains("--no-build --no-restore", script, StringComparison.Ordinal);
        Assert.Contains("Required quality-report coverage evidence is missing", script, StringComparison.Ordinal);
        Assert.Contains("contains no executable lines", script, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://cdn", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$env:USERNAME", script, StringComparison.Ordinal);
        Assert.Contains("$sensitivePrefixes", script, StringComparison.Ordinal);
        Assert.Contains("host-specific absolute path", script, StringComparison.Ordinal);

        string postgresCoverage = File.ReadAllText(postgresCoveragePath);
        Assert.Contains("--collect:XPlat Code Coverage", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("--filter", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("Provider=Postgres", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("SharpAccess.ProviderContractTests.Postgres", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("produced no Coverlet coverage evidence", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("+SharpAccess.Core;+SharpAccess.Sqlite;+SharpAccess.Postgres", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("artifacts/coverage/postgres", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("Write-CanonicalCobertura", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("System.Collections.IDictionary", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("$imported.SetAttribute(\"name\", $assembly)", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("\"SharpAccess.Core\" = Join-Path", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("\"SharpAccess.Sqlite\" = Join-Path", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("\"SharpAccess.Postgres\" = Join-Path", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("Invoke-Report $reports \"artifacts/coverage/core\"", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("Invoke-Report $reports \"artifacts/coverage/sqlite\"", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("ChangedHandwrittenProduction", postgresCoverage, StringComparison.Ordinal);
        Assert.Contains("complexity-report.ps1", postgresCoverage, StringComparison.Ordinal);

        string localCi = File.ReadAllText(localCiPath);
        Assert.Contains("scripts/postgres-quality-coverage.ps1", localCi, StringComparison.Ordinal);
        Assert.Contains("if ($RequirePostgres)", localCi, StringComparison.Ordinal);
        Assert.Contains("if (-not $RequirePostgres)", localCi, StringComparison.Ordinal);
        Assert.Contains("scripts/pack.ps1", localCi, StringComparison.Ordinal);
        Assert.Contains("-SkipSetupTest", localCi, StringComparison.Ordinal);

        string pack = File.ReadAllText(packPath);
        Assert.Contains("[switch]$SkipSetupTest", pack, StringComparison.Ordinal);
        Assert.Contains("if (-not $SkipSetupTest)", pack, StringComparison.Ordinal);
        Assert.Contains("scripts/setup-test.ps1", pack, StringComparison.Ordinal);

        string release = File.ReadAllText(releasePath);
        int qualityIndex = release.IndexOf("scripts/quality-report.ps1", StringComparison.Ordinal);
        int successIndex = release.IndexOf("Release dry run completed successfully", StringComparison.Ordinal);
        Assert.True(qualityIndex >= 0);
        Assert.True(successIndex > qualityIndex);

        XDocument project = XDocument.Load(projectPath);
        Assert.Equal("false", project.Descendants("IsPackable").Single().Value);
        Assert.Equal(
            "SharpAccess.QualityReport.QualityReportEntryPoint",
            project.Descendants("StartupObject").Single().Value);
        string[] packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Contains("Microsoft.Build.Locator", packages);
        Assert.Contains("Microsoft.Build.Framework", packages);
        Assert.Contains("RemoveTransitiveMSBuildFrameworkRuntimeAsset", File.ReadAllText(projectPath), StringComparison.Ordinal);
        Assert.Contains("<ExcludeAssets>runtime</ExcludeAssets>", File.ReadAllText(projectPath), StringComparison.Ordinal);
        Assert.Contains("<PrivateAssets>all</PrivateAssets>", File.ReadAllText(projectPath), StringComparison.Ordinal);
        Assert.Contains("Microsoft.CodeAnalysis.AnalyzerUtilities", packages);
        Assert.Contains("Microsoft.CodeAnalysis.CSharp.Workspaces", packages);
        Assert.Contains("Microsoft.CodeAnalysis.Workspaces.MSBuild", packages);

        string entryPoint = File.ReadAllText(entryPointPath);
        Assert.Contains("QualityReportPostProcessor.Apply", entryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("hotspotDisplayedCount", entryPoint, StringComparison.Ordinal);
        Assert.True(File.ReadAllLines(entryPointPath).Length < 60);

        string program = File.ReadAllText(programPath);
        Assert.Contains("internal static partial class Program", program, StringComparison.Ordinal);
        Assert.Contains("GenerateAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed record QualityPolicy", program, StringComparison.Ordinal);
        Assert.True(File.ReadAllLines(programPath).Length < 180);

        string processor = File.ReadAllText(processorPath);
        Assert.Contains("internal static partial class QualityReportPostProcessor", processor, StringComparison.Ordinal);
        Assert.Contains("public static void Apply", processor, StringComparison.Ordinal);

        string infrastructure = File.ReadAllText(processorInfrastructurePath);
        Assert.Contains("RefreshManifest", infrastructure, StringComparison.Ordinal);

        string hotspots = File.ReadAllText(hotspotPath);
        Assert.Contains("hotspotDisplayedCount", processor, StringComparison.Ordinal);
        Assert.Contains("SecuritySensitiveUncovered", hotspots, StringComparison.Ordinal);

        string metrics = File.ReadAllText(metricsPath);
        Assert.Contains("percentile05", metrics, StringComparison.Ordinal);
        Assert.Contains("matchStatusCounts", processor, StringComparison.Ordinal);
        Assert.Contains("sourceFiles", metrics, StringComparison.Ordinal);

        Assert.Contains("BuildHotspotSection", File.ReadAllText(htmlPath), StringComparison.Ordinal);
        Assert.Contains("QualityThresholds", File.ReadAllText(policyOwnershipPath), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpAccess.sln"))) { return directory.FullName; }
                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
