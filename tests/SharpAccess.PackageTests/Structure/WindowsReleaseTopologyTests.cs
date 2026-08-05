using System.Diagnostics;
using System.Xml.Linq;

namespace SharpAccess.PackageTests;

public sealed class WindowsReleaseTopologyTests
{
    [Fact]
    public void RepositoryContainsOnlyTheActiveProviderCohort()
    {
        string root = FindRepositoryRoot();
        string[] providers = Directory
            .EnumerateDirectories(Path.Combine(root, "providers"), "SharpAccess.*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        string[] expectedProviders = ["SharpAccess.Postgres", "SharpAccess.Sqlite"];

        Assert.Equal(expectedProviders, providers);
        Assert.False(Directory.Exists(Path.Combine(root, "providers", "SharpAccess.SqlServer")));
        Assert.False(Directory.Exists(Path.Combine(root, "providers", "SharpAccess.MySql")));

        XDocument status = XDocument.Load(Path.Combine(root, "eng", "ProviderStatus.props"));
        Assert.Equal("Supported", status.Descendants("SharpAccessCoreStatus").Single().Value);
        Assert.Equal("Supported", status.Descendants("SharpAccessSqliteStatus").Single().Value);
        Assert.Equal("Supported", status.Descendants("SharpAccessPostgresStatus").Single().Value);
        Assert.Empty(status.Descendants("SharpAccessSqlServerStatus"));
        Assert.Empty(status.Descendants("SharpAccessMySqlStatus"));
    }

    [Fact]
    public void RepositoryAutomationIsWindowsPowerShellOnlyAndContainerFree()
    {
        string root = FindRepositoryRoot();
        string[] trackedFiles = GetTrackedFiles(root);

        Assert.DoesNotContain(trackedFiles, path => path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(trackedFiles, path => path.StartsWith("eng/containers/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(trackedFiles, path => Path.GetFileName(path).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(trackedFiles, path =>
            Path.GetFileName(path).Contains("compose", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)));

        foreach (string script in Directory.EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(script);
            Assert.True(source.StartsWith("#Requires -Version 7.0", StringComparison.Ordinal), $"{script} must require PowerShell 7.");
            Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        }

        foreach (string workflow in Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(workflow);
            Assert.DoesNotContain("ubuntu-latest", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("macos-latest", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("shell: bash", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("docker", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("services:", source, StringComparison.OrdinalIgnoreCase);
            if (source.Contains("runs-on:", StringComparison.Ordinal))
            {
                Assert.Contains("runs-on: windows-latest", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RetiredProvidersAppearOnlyInRoadmapAndDecisionRecords()
    {
        string root = FindRepositoryRoot();
        string roadmap = File.ReadAllText(Path.Combine(root, "docs", "ROADMAP.md"));
        Assert.Contains("SQL Server", roadmap, StringComparison.Ordinal);
        Assert.Contains("MySQL", roadmap, StringComparison.Ordinal);
        Assert.Contains("future roadmap candidates", roadmap, StringComparison.OrdinalIgnoreCase);

        string[] trackedFiles = GetTrackedFiles(root);
        string[] activeFiles = trackedFiles
            .Where(path => path.StartsWith("src/", StringComparison.Ordinal)
                || path.StartsWith("providers/", StringComparison.Ordinal)
                || path.StartsWith("samples/", StringComparison.Ordinal)
                || path.StartsWith("tools/", StringComparison.Ordinal)
                || path.StartsWith("scripts/", StringComparison.Ordinal))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, "scripts/verify-structure.ps1", StringComparison.Ordinal))
            .ToArray();

        foreach (string relativePath in activeFiles)
        {
            string source = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain("SharpAccess.SqlServer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SharpAccess.MySql", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddSqlServerAccess", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddMySqlAccess", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SolutionAndCentralDependenciesExcludeRetiredProviders()
    {
        string root = FindRepositoryRoot();
        string solution = File.ReadAllText(Path.Combine(root, "SharpAccess.sln"));
        string packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));

        Assert.DoesNotContain("SharpAccess.SqlServer", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("SharpAccess.MySql", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("MySqlConnector", packages, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSchema.Net", packages, StringComparison.Ordinal);
    }

    private static string[] GetTrackedFiles(string root)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("ls-files");

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to enumerate tracked files: {error.Trim()}");
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
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