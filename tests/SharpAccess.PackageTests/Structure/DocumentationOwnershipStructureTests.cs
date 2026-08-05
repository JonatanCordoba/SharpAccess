using System.Text.RegularExpressions;

namespace SharpAccess.PackageTests;

public sealed partial class DocumentationOwnershipStructureTests
{
    [Fact]
    public void PackageProjectsDoNotSuppressMissingXmlDocumentation()
    {
        string root = FindRepositoryRoot();
        string[] packageProjects =
        [
            Path.Combine(root, "src", "SharpAccess.Core", "SharpAccess.Core.csproj"),
            Path.Combine(root, "providers", "SharpAccess.Sqlite", "SharpAccess.Sqlite.csproj"),
            Path.Combine(root, "providers", "SharpAccess.Postgres", "SharpAccess.Postgres.csproj")
        ];

        foreach (string projectPath in packageProjects)
        {
            string project = File.ReadAllText(projectPath);
            Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", project, StringComparison.Ordinal);
            Assert.DoesNotContain("CS1591", project, StringComparison.Ordinal);
        }

        string[] packageSourceRoots =
        [
            Path.Combine(root, "src", "SharpAccess.Core"),
            Path.Combine(root, "providers", "SharpAccess.Sqlite"),
            Path.Combine(root, "providers", "SharpAccess.Postgres")
        ];

        foreach (string sourceRoot in packageSourceRoots)
        {
            foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                Assert.DoesNotContain("#pragma warning disable CS1591", File.ReadAllText(sourcePath), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DocumentationIndexDefinesMutableFactOwners()
    {
        string root = FindRepositoryRoot();
        string indexPath = Path.Combine(root, "docs", "README.md");
        Assert.True(File.Exists(indexPath), "docs/README.md must be the documentation entry point.");
        string index = File.ReadAllText(indexPath);

        Assert.Contains("eng/ProviderStatus.props", index, StringComparison.Ordinal);
        Assert.Contains("docs/ROADMAP.md", index, StringComparison.Ordinal);
        Assert.Contains("docs/PROVIDER-PARITY-EVIDENCE.md", index, StringComparison.Ordinal);
        Assert.Contains("docs/RELEASE-CHECKLIST.md", index, StringComparison.Ordinal);
        Assert.Contains("Pre-release patch plans", index, StringComparison.Ordinal);
        Assert.Contains("duplicate release ledgers", index, StringComparison.Ordinal);
        Assert.Contains("clean public tree", index, StringComparison.Ordinal);
    }

    [Fact]
    public void AdrIndexAndFilesAgree()
    {
        string root = FindRepositoryRoot();
        string adrRoot = Path.Combine(root, "docs", "adr");
        string index = File.ReadAllText(Path.Combine(adrRoot, "README.md"));
        string[] indexed = AdrLinkPattern()
            .Matches(index)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] files = Directory
            .EnumerateFiles(adrRoot, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "README.md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(files, indexed);
        Assert.Equal(indexed.Length, indexed.Select(GetAdrNumber).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("0012-clean-release-repository.md", indexed, StringComparer.Ordinal);
        Assert.Contains("0019-windows-only-release-toolchain.md", indexed, StringComparer.Ordinal);
        Assert.Contains("0020-active-provider-cohort.md", indexed, StringComparer.Ordinal);
        Assert.Contains("superseded", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PullRequestTemplateRequiresConcreteEvidence()
    {
        string root = FindRepositoryRoot();
        string template = File.ReadAllText(Path.Combine(root, ".github", "pull_request_template.md"));

        Assert.Contains("Head commit SHA", template, StringComparison.Ordinal);
        Assert.Contains("Post-commit result", template, StringComparison.Ordinal);
        Assert.Contains("Environment-blocked", template, StringComparison.Ordinal);
        Assert.Contains("PROJECT_MANIFEST.md", template, StringComparison.Ordinal);
    }

    [Fact]
    public void RoadmapSeparatesImplementationEvidenceAndFutureProviders()
    {
        string root = FindRepositoryRoot();
        string roadmap = File.ReadAllText(Path.Combine(root, "docs", "ROADMAP.md"));

        Assert.Contains("Implementation complete and merged", roadmap, StringComparison.Ordinal);
        Assert.Contains("Evidence complete", roadmap, StringComparison.Ordinal);
        Assert.Contains("A merged pull request or configured workflow does not establish evidence completion by itself", roadmap, StringComparison.Ordinal);
        Assert.Contains("SQL Server", roadmap, StringComparison.Ordinal);
        Assert.Contains("MySQL", roadmap, StringComparison.Ordinal);
        Assert.Contains("future roadmap candidates", roadmap, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows-only", roadmap, StringComparison.Ordinal);
        Assert.Contains("PowerShell 7", roadmap, StringComparison.Ordinal);
    }

    private static string GetAdrNumber(string fileName)
    {
        int separator = fileName.IndexOf('-', StringComparison.Ordinal);
        Assert.True(separator > 0, $"ADR filename lacks a numeric prefix: {fileName}");
        return fileName[..separator];
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

    [GeneratedRegex(@"\]\((\d{4}-[^)]+\.md)\)", RegexOptions.CultureInvariant)]
    private static partial Regex AdrLinkPattern();
}
