using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpAccess.QualityReport;

internal static partial class Program
{
    private sealed record HashEntry(string Path, string Sha256);
    private sealed record EvidenceManifest(
        int SchemaVersion,
        string Repository,
        string Revision,
        int MetricSchemaVersion,
        IReadOnlyDictionary<string, string> ToolVersions,
        IReadOnlyList<HashEntry> CoverageInputs,
        IReadOnlyList<HashEntry> CodeMetricInputs,
        IReadOnlyList<HashEntry> Outputs)
    {
        public static EvidenceManifest Create(
            string root,
            string output,
            Arguments options,
            string policyPath,
            string coveragePath,
            string complexityPath,
            IReadOnlyList<ProjectPolicy> projects,
            IReadOnlyDictionary<string, string> toolVersions)
        {
            List<string> codeInputs =
            [
                Path.Combine(root, "SharpAccess.sln"),
                policyPath
            ];
            foreach (ProjectPolicy project in projects)
            {
                string projectPath = Path.Combine(root, project.Path.Replace('/', Path.DirectorySeparatorChar));
                codeInputs.Add(projectPath);
                string projectDirectory = Path.GetDirectoryName(projectPath)!;
                codeInputs.AddRange(Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                   && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                   && !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)));
            }

            string[] outputFiles = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFileName(path), "manifest.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => NormalizeRelative(root, path), Ordinal)
                .ToArray();

            return new EvidenceManifest(
                1,
                options.RepositoryUrl,
                options.Revision,
                1,
                toolVersions,
                HashEntries(root, [coveragePath, complexityPath]),
                HashEntries(root, codeInputs),
                HashEntries(root, outputFiles));
        }

        private static HashEntry[] HashEntries(string root, IEnumerable<string> paths)
            => paths.Where(File.Exists)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new HashEntry(NormalizeRelative(root, path), Sha256(path)))
                .OrderBy(entry => entry.Path, Ordinal)
                .ToArray();
    }
}
