using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SharpAccess.QualityReport;

internal static partial class QualityReportPostProcessor
{
    public static void Apply(string[] args)
    {
        IReadOnlyDictionary<string, string> arguments = ParseArguments(args);
        string repositoryRoot = Path.GetFullPath(GetRequired(arguments, "repository-root"));
        string outputDirectory = Path.GetFullPath(GetRequired(arguments, "output"));
        string policyPath = Path.GetFullPath(GetRequired(arguments, "policy"));
        string metricsPath = Path.Combine(outputDirectory, "metrics.json");
        string indexPath = Path.Combine(outputDirectory, "index.html");
        string manifestPath = Path.Combine(outputDirectory, "manifest.json");

        RequireFile(metricsPath);
        RequireFile(indexPath);
        RequireFile(manifestPath);
        RequireFile(policyPath);

        QualityThresholds thresholds = QualityThresholds.Load(policyPath);
        JsonObject metrics = ParseObject(metricsPath);
        metrics["schemaVersion"] = 2;
        JsonArray members = metrics["members"] as JsonArray
            ?? throw new InvalidOperationException("Quality metrics contain no members array.");
        JsonArray types = metrics["types"] as JsonArray
            ?? throw new InvalidOperationException("Quality metrics contain no types array.");
        JsonArray projects = metrics["projects"] as JsonArray
            ?? throw new InvalidOperationException("Quality metrics contain no projects array.");

        List<HotspotCandidate> allHotspots = BuildHotspots(members, thresholds);
        int displayedHotspots = Math.Min(thresholds.TopHotspots, allHotspots.Count);
        JsonArray hotspotRows = [];
        foreach (HotspotCandidate hotspot in allHotspots.Take(displayedHotspots))
        {
            hotspotRows.Add(hotspot.ToJson());
        }
        metrics["hotspots"] = hotspotRows;

        JsonObject summary = metrics["summary"] as JsonObject
            ?? throw new InvalidOperationException("Quality metrics contain no summary object.");
        summary["hotspotCount"] = allHotspots.Count;
        summary["hotspotDisplayedCount"] = displayedHotspots;
        summary["hotspotSeverityCounts"] = CountByClassification(allHotspots);
        EnrichMaintainabilitySummary(summary, members, thresholds);

        JsonObject matchStatusCounts = CountByStringProperty(members, "matchStatus");
        metrics["matchStatusCounts"] = matchStatusCounts;
        JsonObject coverageScope = BuildCoverageScope(projects);
        metrics["coverageScope"] = coverageScope;
        JsonArray partialTypes = EnrichTypeSourceFiles(types, members);
        metrics["partialTypes"] = partialTypes;

        JsonObject definitions;
        if (metrics["definitions"] is JsonObject existingDefinitions)
        {
            definitions = existingDefinitions;
        }
        else
        {
            definitions = new JsonObject();
            metrics["definitions"] = definitions;
        }
        definitions["coverageScope"] = "Production assemblies included in the repository aggregate; report generation fails when any required assembly is absent or has no executable lines.";
        definitions["hotspotCount"] = "Total qualifying hotspots before the configured display limit; hotspotDisplayedCount is the number retained in the top-N table.";
        definitions["hotspotReasons"] = "Machine-readable reason codes identify whether remediation is primarily coverage, branch coverage, complexity, maintainability, coupling, or security-sensitive uncovered behavior.";
        definitions["maintainabilityTail"] = "For maintainability index, the adverse tail is represented by the 5th and 10th percentiles because lower values are worse.";
        definitions["sourceFiles"] = "Every distinct contributing member source file for a type; partial types may therefore list multiple files.";
        definitions["matchStatusCounts"] = "Counts of Matched, ComplexityMatched, and RoslynOnly member evidence states. RoslynOnly includes declarations and symbols without executable coverage evidence and is not automatically a binding defect.";

        WriteJson(metricsPath, metrics);

        string html = File.ReadAllText(indexPath);
        html = ReplaceHotspotCard(html, allHotspots.Count, displayedHotspots);
        html = InsertCoverageScopeBanner(html, coverageScope);
        html = ReplaceSection(html, "hotspots", BuildHotspotSection(allHotspots.Take(displayedHotspots)));
        html = InsertBeforeDefinitions(
            html,
            BuildDiagnosticsSection(summary, matchStatusCounts, coverageScope, thresholds) +
            BuildPartialTypesSection(partialTypes));
        WriteUtf8(indexPath, html);

        RefreshManifest(repositoryRoot, outputDirectory, manifestPath);
    }
}
