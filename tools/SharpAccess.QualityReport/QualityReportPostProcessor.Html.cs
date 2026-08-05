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
    private static string ReplaceHotspotCard(string html, int total, int displayed)
    {
        const string StartMarker = "<span>Hotspots</span><strong>";
        const string MiddleMarker = "</strong><small>";
        const string EndMarker = "</small>";
        int start = html.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return html;
        }
        int valueStart = start + StartMarker.Length;
        int middle = html.IndexOf(MiddleMarker, valueStart, StringComparison.Ordinal);
        int end = middle < 0 ? -1 : html.IndexOf(EndMarker, middle + MiddleMarker.Length, StringComparison.Ordinal);
        if (middle < 0 || end < 0)
        {
            return html;
        }
        string replacement = total.ToString(CultureInfo.InvariantCulture) + MiddleMarker +
            displayed.ToString(CultureInfo.InvariantCulture) + " displayed from the qualifying inventory";
        return html[..valueStart] + replacement + html[end..];
    }
    private static string InsertCoverageScopeBanner(string html, JsonObject coverageScope)
    {
        JsonArray included = coverageScope["includedAssemblies"] as JsonArray ?? [];
        string assemblies = string.Join(", ", included.Select(node => node?.GetValue<string>() ?? string.Empty));
        string banner = $"<p class=\"coverage-scope\"><strong>Coverage scope complete:</strong> {E(assemblies)}. Missing required production evidence is a fatal report error.</p>";
        const string marker = "<p><a href=\"coverage/index.html\">";
        int start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return html;
        }
        int end = html.IndexOf("</p>", start, StringComparison.Ordinal);
        return end < 0 ? html : html.Insert(end + 4, banner);
    }
    private static string BuildHotspotSection(IEnumerable<HotspotCandidate> hotspots)
    {
        StringBuilder html = new();
        html.Append("<section id=\"hotspots\"><h2>Unified hotspots</h2><div class=\"table-wrap\"><table><thead><tr>");
        string[] headings = ["Classification", "Reasons", "Project", "Type", "Member", "CRAP", "Uncovered branches", "Complexity", "Maintainability", "Class coupling", "Location"];
        foreach (string heading in headings)
        {
            html.Append("<th>").Append(E(heading)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");
        foreach (HotspotCandidate row in hotspots)
        {
            html.Append("<tr class=\"").Append(E(row.Classification.ToLowerInvariant())).Append("\">")
                .Append(Cell(row.Classification))
                .Append(Cell(string.Join(", ", row.Reasons)))
                .Append(Cell(row.Project))
                .Append(Cell(row.Type))
                .Append(Cell(row.Member))
                .Append(Cell(Number(row.CrapScore)))
                .Append(Cell(row.UncoveredBranches.ToString(CultureInfo.InvariantCulture)))
                .Append(Cell(row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture)))
                .Append(Cell(row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture)))
                .Append(Cell(row.ClassCoupling.ToString(CultureInfo.InvariantCulture)))
                .Append(Cell(Location(row.File, row.StartLine)))
                .Append("</tr>");
        }
        html.Append("</tbody></table></div></section>");
        return html.ToString();
    }
    private static string BuildDiagnosticsSection(
        JsonObject summary,
        JsonObject matchStatusCounts,
        JsonObject coverageScope,
        QualityThresholds thresholds)
    {
        JsonObject severity = summary["hotspotSeverityCounts"] as JsonObject ?? new JsonObject();
        JsonObject maintainability = summary["maintainabilityIndex"] as JsonObject ?? new JsonObject();
        StringBuilder html = new();
        html.Append("<section id=\"quality-diagnostics\"><h2>Quality-report diagnostics</h2><div class=\"cards\">");
        DiagnosticCard(html, "Critical hotspots", GetInt(severity, "critical").ToString(CultureInfo.InvariantCulture), "Full qualifying inventory");
        DiagnosticCard(html, "Warning hotspots", GetInt(severity, "warning").ToString(CultureInfo.InvariantCulture), "Full qualifying inventory");
        DiagnosticCard(html, "Displayed hotspots", GetInt(summary, "hotspotDisplayedCount").ToString(CultureInfo.InvariantCulture), $"Top-N limit {thresholds.TopHotspots.ToString(CultureInfo.InvariantCulture)}");
        DiagnosticCard(html, "Maintainability p05", Number(GetNullableDouble(maintainability, "percentile05")), "Lower values are worse");
        DiagnosticCard(html, "Maintainability p10", Number(GetNullableDouble(maintainability, "percentile10")), "Lower values are worse");
        DiagnosticCard(html, "Coverage scope", GetBool(coverageScope, "complete") ? "Complete" : "Incomplete", "All production projects required");
        html.Append("</div><h3>Symbol evidence states</h3><div class=\"table-wrap\"><table><thead><tr><th>Status</th><th>Count</th></tr></thead><tbody>");
        foreach ((string status, JsonNode? count) in matchStatusCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            html.Append("<tr>").Append(Cell(status)).Append(Cell(count?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "0")).Append("</tr>");
        }
        html.Append("</tbody></table></div></section>");
        return html.ToString();
    }
    private static string BuildPartialTypesSection(JsonArray partialTypes)
    {
        if (partialTypes.Count == 0)
        {
            return string.Empty;
        }
        StringBuilder html = new();
        html.Append("<section id=\"partial-types\"><h2>Partial-type source attribution</h2><div class=\"table-wrap\"><table><thead><tr><th>Project</th><th>Type</th><th>Contributing files</th></tr></thead><tbody>");
        foreach (JsonObject type in partialTypes.OfType<JsonObject>())
        {
            JsonArray files = type["sourceFiles"] as JsonArray ?? [];
            html.Append("<tr>")
                .Append(Cell(GetString(type, "project")))
                .Append(Cell(GetString(type, "type")))
                .Append(Cell(string.Join(", ", files.Select(file => file?.GetValue<string>() ?? string.Empty))))
                .Append("</tr>");
        }
        html.Append("</tbody></table></div></section>");
        return html.ToString();
    }
    private static string ReplaceSection(string html, string id, string replacement)
    {
        string marker = $"<section id=\"{id}\">";
        int start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Quality-report HTML section was not found: {id}");
        }
        int end = html.IndexOf("</section>", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException($"Quality-report HTML section is incomplete: {id}");
        }
        end += "</section>".Length;
        return html[..start] + replacement + html[end..];
    }
    private static string InsertBeforeDefinitions(string html, string content)
    {
        const string marker = "<section><h2>Definitions and provenance</h2>";
        int index = html.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("Quality-report definitions section was not found.");
        }
        return html.Insert(index, content);
    }
    private static void DiagnosticCard(StringBuilder html, string title, string value, string detail)
        => html.Append("<div class=\"card\"><span>").Append(E(title)).Append("</span><strong>")
            .Append(E(value)).Append("</strong><small>").Append(E(detail)).Append("</small></div>");
    private static string Cell(string value) => $"<td>{E(value)}</td>";
    private static string Location(string file, int line)
        => string.IsNullOrEmpty(file) ? "n/a" : line > 0 ? $"{file}:{line.ToString(CultureInfo.InvariantCulture)}" : file;
    private static string Number(double? value)
        => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";
    private static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
}
