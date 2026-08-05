using System.Xml.Linq;

namespace SharpAccess.PackageTests;

public sealed class ReleaseMetadataStructureTests
{
    private static readonly string[] PackageProjects =
    [
        "src/SharpAccess.Core/SharpAccess.Core.csproj",
        "providers/SharpAccess.Sqlite/SharpAccess.Sqlite.csproj",
        "providers/SharpAccess.Postgres/SharpAccess.Postgres.csproj"
    ];

    [Fact]
    public void VersionIsOwnedByOneImportedPropertyFile()
    {
        string root = FindRepositoryRoot();
        string versionPath = Path.Combine(root, "eng", "Version.props");
        XDocument version = XDocument.Load(versionPath);

        Assert.Equal(
            "0.9.0-rc.1",
            version.Descendants("SharpAccessVersion").Single().Value.Trim());
        Assert.Equal(
            "$(SharpAccessVersion)",
            version.Descendants("Version").Single().Value.Trim());

        string directoryBuild = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        Assert.Contains(
            "eng/Version.props",
            directoryBuild.Replace('\\', '/'),
            StringComparison.Ordinal);

        foreach (string relativeProject in PackageProjects)
        {
            XDocument project = XDocument.Load(
                Path.Combine(
                    root,
                    relativeProject.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Empty(project.Descendants("Version"));
        }
    }

    [Fact]
    public void SupportedPackagesProduceDocumentationAndReadmeMetadata()
    {
        string root = FindRepositoryRoot();

        foreach (string relativeProject in PackageProjects)
        {
            XDocument project = XDocument.Load(
                Path.Combine(
                    root,
                    relativeProject.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(
                "true",
                project.Descendants("GenerateDocumentationFile").Single().Value.Trim());
            Assert.Equal(
                "README.md",
                project.Descendants("PackageReadmeFile").Single().Value.Trim());
            Assert.Contains(
                project.Descendants("None"),
                item => string.Equals(
                            item.Attribute("Pack")?.Value,
                            "true",
                            StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        item.Attribute("Link")?.Value,
                        "README.md",
                        StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ReleaseCandidateWorkflowUsesAuthoritativeVersion()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "release-candidate.yml"));

        Assert.Contains("default: 0.9.0-rc.1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.0-dev", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedComplexityPolicyIncludesEverySupportedAssembly()
    {
        string root = FindRepositoryRoot();
        XDocument policy = XDocument.Load(
            Path.Combine(root, "eng", "ComplexityPolicy.props"));
        XElement gate = policy.Descendants("ComplexityGate").Single();

        Assert.Equal("SupportedProduction", gate.Attribute("Include")?.Value);
        Assert.Equal("Ratchet", gate.Attribute("Enforcement")?.Value);
        Assert.Equal(
            "SharpAccess.Core;SharpAccess.Sqlite;SharpAccess.Postgres",
            gate.Attribute("Assemblies")?.Value);
    }

    [Fact]
    public void InternalBuildModuleOwnsSharedReleaseHelpers()
    {
        string root = FindRepositoryRoot();
        string moduleRoot = Path.Combine(root, "scripts", "SharpAccess.Build");
        string manifestPath = Path.Combine(moduleRoot, "SharpAccess.Build.psd1");
        string modulePath = Path.Combine(moduleRoot, "SharpAccess.Build.psm1");

        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(modulePath));

        string module = File.ReadAllText(modulePath);
        foreach (string function in new[]
        {
            "Resolve-SharpAccessRepositoryRoot",
            "Get-SharpAccessVersion",
            "Get-SharpAccessRevision",
            "Invoke-SharpAccessDotNet",
            "Write-SharpAccessUtf8NoBom"
        })
        {
            Assert.Contains($"function {function}", module, StringComparison.Ordinal);
        }

        foreach (string script in new[]
        {
            "pack.ps1",
            "package-smoke.ps1",
            "release-dry-run.ps1",
            "release-candidate.ps1",
            "verify-local.ps1"
        })
        {
            string content = File.ReadAllText(Path.Combine(root, "scripts", script));
            Assert.Contains(
                "SharpAccess.Build/SharpAccess.Build.psd1",
                content.Replace('\\', '/'),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DocumentationHasOneQualityGateOwnerAndNoStaleLedgers()
    {
        string root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "QUALITY-GATES.md")));
        Assert.False(File.Exists(Path.Combine(root, "IMPLEMENTATION_STATUS.md")));
        Assert.False(File.Exists(Path.Combine(root, "docs", "POSTGRES.md")));

        foreach (string redirect in new[]
        {
            "COVERAGE-AND-TEST-GATES.md",
            "VERIFICATION-AND-COMPLEXITY.md",
            "QUALITY-OBJECTIVES.md"
        })
        {
            string content = File.ReadAllText(Path.Combine(root, "docs", redirect));
            Assert.Contains("QUALITY-GATES.md", content, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException(
            "Could not locate the SharpAccess repository root.");
    }
}
