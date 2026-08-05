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
    public static async Task<int> Main(string[] args)
    {
        try
        {
            Arguments options = Arguments.Parse(args);
            await GenerateAsync(options).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
    private static async Task GenerateAsync(Arguments options)
    {
        string root = Path.GetFullPath(options.RepositoryRoot);
        string output = Path.GetFullPath(options.OutputDirectory);
        string policyPath = Path.GetFullPath(options.PolicyPath);
        string coveragePath = Path.GetFullPath(options.CoveragePath);
        string complexityPath = Path.GetFullPath(options.ComplexityPath);

        RequireFile(Path.Combine(root, "SharpAccess.sln"));
        RequireFile(policyPath);
        RequireFile(coveragePath);
        RequireFile(complexityPath);

        string actualRevision = RunGit(root, "rev-parse", "HEAD");
        if (!string.Equals(actualRevision, options.Revision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Quality report revision mismatch. Expected {options.Revision}; checked-out HEAD is {actualRevision}.");
        }

        QualityPolicy policy = QualityPolicy.Load(policyPath);
        Directory.CreateDirectory(output);

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        List<WorkspaceDiagnostic> workspaceDiagnostics = [];
        using MSBuildWorkspace workspace = MSBuildWorkspace.Create(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Configuration"] = "Release",
            ["RestoreLockedMode"] = "true"
        });
        using WorkspaceEventRegistration workspaceFailedRegistration = workspace.RegisterWorkspaceFailedHandler(eventArgs => workspaceDiagnostics.Add(eventArgs.Diagnostic));

        List<ProjectAnalysis> projects = [];
        HashSet<string> internalAssemblies = policy.Projects
            .Select(project => project.Assembly)
            .ToHashSet(StringComparer.Ordinal);

        foreach (ProjectPolicy projectPolicy in policy.Projects.OrderBy(project => project.Assembly, Ordinal))
        {
            string projectPath = Path.Combine(root, projectPolicy.Path.Replace('/', Path.DirectorySeparatorChar));
            RequireFile(projectPath);
            Project project = await workspace.OpenProjectAsync(projectPath).ConfigureAwait(false);
            Compilation compilation = await project.GetCompilationAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Roslyn compilation is unavailable for {projectPolicy.Path}.");

            Diagnostic[] errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .OrderBy(diagnostic => diagnostic.Id, Ordinal)
                .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().Path, Ordinal)
                .ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Roslyn compilation has errors for {projectPolicy.Path}:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
            }

#pragma warning disable CS0618
            CodeAnalysisMetricData metricData =
                await CodeAnalysisMetricData.ComputeAsync(compilation, CancellationToken.None).ConfigureAwait(false);
#pragma warning restore CS0618

            projects.Add(ProjectAnalysis.Create(root, projectPolicy, metricData, internalAssemblies));
        }

        WorkspaceDiagnostic[] failures = workspaceDiagnostics
            .Where(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            .OrderBy(diagnostic => diagnostic.Message, Ordinal)
            .ToArray();
        if (failures.Length != 0)
        {
            throw new InvalidOperationException(
                "MSBuildWorkspace reported failures:" + Environment.NewLine +
                string.Join(Environment.NewLine, failures.Select(failure => failure.Message)));
        }

        CoverageDataset coverage = CoverageDataset.Load(coveragePath, root);
        ComplexityDataset complexity = ComplexityDataset.Load(complexityPath);
        ReportDataset dataset = ReportDataset.Create(
            options,
            policy,
            projects,
            coverage,
            complexity,
            GetToolVersions(options));

        string metricsPath = Path.Combine(output, "metrics.json");
        string indexPath = Path.Combine(output, "index.html");
        WriteUtf8(metricsPath, JsonSerializer.Serialize(dataset, JsonOptions) + "\n");
        WriteUtf8(indexPath, HtmlReport.Write(dataset));

        EvidenceManifest manifest = EvidenceManifest.Create(
            root,
            output,
            options,
            policyPath,
            coveragePath,
            complexityPath,
            policy.Projects,
            GetToolVersions(options));

        string manifestPath = Path.Combine(output, "manifest.json");
        WriteUtf8(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n");

        Console.WriteLine($"Engineering-quality report: {NormalizeRelative(root, indexPath)}");
        Console.WriteLine($"Revision: {actualRevision}");
        Console.WriteLine($"Projects: {dataset.Projects.Count}; namespaces: {dataset.Namespaces.Count}; types: {dataset.Types.Count}; members: {dataset.Members.Count}.");
    }
}
