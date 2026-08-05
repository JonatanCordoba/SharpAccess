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
    private static class HtmlReport
    {
        public static string Write(ReportDataset data)
        {
            StringBuilder html = new();
            html.Append("""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>SharpAccess engineering-quality report</title>
<style>
:root{font-family:Segoe UI,Arial,sans-serif;color:#172033;background:#f5f7fb}
body{margin:0}.page{max-width:1500px;margin:auto;padding:24px}
h1,h2{margin:.2em 0}.muted{color:#5d687b}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px;margin:20px 0}
.card,section{background:white;border:1px solid #d9deea;border-radius:10px;padding:16px;box-shadow:0 2px 8px #1f29370d}
.card strong{font-size:1.6rem;display:block}.toolbar{display:flex;gap:12px;align-items:center;margin:12px 0}
input{padding:8px 10px;min-width:280px;border:1px solid #bbc3d2;border-radius:6px}
.table-wrap{overflow:auto;max-height:620px;border:1px solid #e1e5ee;border-radius:8px}
table{border-collapse:collapse;width:100%;font-size:.88rem}th,td{padding:8px 10px;border-bottom:1px solid #e6e9f0;text-align:left;white-space:nowrap}
th{position:sticky;top:0;background:#eef2f8;cursor:pointer}.critical{background:#ffe6e6}.warning{background:#fff5d6}.information{background:#eaf3ff}
code{font-family:Cascadia Mono,Consolas,monospace}.pill{padding:2px 7px;border-radius:999px;background:#e8edf6}
a{color:#1557b0}details{margin:10px 0}footer{margin:30px 0;color:#5d687b}
</style>
</head>
<body><div class="page">
""");
            html.Append("<h1>SharpAccess engineering-quality report</h1>");
            html.Append("<p class=\"muted\">Exact revision <code>").Append(E(data.Revision))
                .Append("</code> · repository <code>").Append(E(data.Repository)).Append("</code> · enforcement <span class=\"pill\">")
                .Append(E(data.Enforcement)).Append("</span></p>");
            html.Append("<p><a href=\"coverage/index.html\">Open detailed line-by-line coverage</a> · <a href=\"metrics.json\">Machine-readable metrics</a> · <a href=\"manifest.json\">Evidence manifest</a></p>");

            CoverageSummary c = data.Summary.Coverage;
            html.Append("<div class=\"cards\">");
            Card(html, "Line coverage", Percent(c.LineCoverage), $"{c.CoveredLines}/{c.TotalLines} executable lines");
            Card(html, "Branch coverage", Percent(c.BranchCoverage), $"{c.CoveredBranches}/{c.TotalBranches} branches");
            Card(html, "CRAP max / p95", $"{Number(data.Summary.CrapScore.Maximum)} / {Number(data.Summary.CrapScore.Percentile95)}", "Executable methods only");
            Card(html, "Cyclomatic max / p95", $"{Number(data.Summary.CyclomaticComplexity.Maximum)} / {Number(data.Summary.CyclomaticComplexity.Percentile95)}", "Roslyn source metrics");
            Card(html, "Maintainability minimum", Number(data.Summary.MaintainabilityIndex.Minimum), "0–100; higher is better");
            Card(html, "Class coupling max", Number(data.Summary.ClassCoupling.Maximum), "Distinct referenced types");
            Card(html, "Hotspots", data.Summary.HotspotCount.ToString(CultureInfo.InvariantCulture), "Unified informational inventory");
            html.Append("</div>");

            SectionStart(html, "Unified hotspots", "hotspots");
            Table(html,
                ["Classification","Project","Type","Member","CRAP","Uncovered branches","Complexity","Maintainability","Class coupling","Location"],
                data.Hotspots.Select(row => new[]
                {
                    row.Classification,row.Project,row.Type,row.Member,Number(row.CrapScore),
                    row.UncoveredBranches.ToString(CultureInfo.InvariantCulture),
                    row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture),
                    row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture),
                    row.ClassCoupling.ToString(CultureInfo.InvariantCulture),
                    Location(row.File,row.StartLine)
                }),
                rowClassIndex: 0);
            SectionEnd(html);

            SectionStart(html, "Projects", "projects");
            Table(html,
                ["Assembly","Line","Branch","Complexity","Maintainability","Class coupling","Ca","Ce","Instability","External deps"],
                data.Projects.Select(row => new[]
                {
                    row.Assembly,Percent(row.Coverage.LineCoverage),Percent(row.Coverage.BranchCoverage),
                    row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture),
                    row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture),
                    row.ClassCoupling.ToString(CultureInfo.InvariantCulture),
                    row.AfferentCoupling.ToString(CultureInfo.InvariantCulture),
                    row.EfferentCoupling.ToString(CultureInfo.InvariantCulture),
                    Number(row.Instability),
                    row.ExternalDependencies.ToString(CultureInfo.InvariantCulture)
                }));
            SectionEnd(html);

            SectionStart(html, "Namespaces", "namespaces");
            Table(html,
                ["Project","Namespace","Line","Branch","Complexity","Maintainability","Class coupling","Ca","Ce","Instability","External deps"],
                data.Namespaces.Select(row => new[]
                {
                    row.Project,row.Namespace,Percent(row.Coverage.LineCoverage),Percent(row.Coverage.BranchCoverage),
                    row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture),
                    row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture),
                    row.ClassCoupling.ToString(CultureInfo.InvariantCulture),
                    row.AfferentCoupling.ToString(CultureInfo.InvariantCulture),
                    row.EfferentCoupling.ToString(CultureInfo.InvariantCulture),
                    Number(row.Instability),
                    row.ExternalDependencies.ToString(CultureInfo.InvariantCulture)
                }));
            SectionEnd(html);

            SectionStart(html, "Types", "types");
            Table(html,
                ["Project","Namespace","Type","Line","Branch","Complexity","Maintainability","Class coupling","Location"],
                data.Types.Select(row => new[]
                {
                    row.Project,row.Namespace,row.Type,Percent(row.Coverage.LineCoverage),Percent(row.Coverage.BranchCoverage),
                    row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture),
                    row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture),
                    row.ClassCoupling.ToString(CultureInfo.InvariantCulture),
                    Location(row.File,row.StartLine)
                }));
            SectionEnd(html);

            SectionStart(html, "Members", "members");
            Table(html,
                ["Project","Namespace","Type","Member","Kind","Line","Branch","CRAP","Complexity","Maintainability","Class coupling","Match","Location"],
                data.Members.Select(row => new[]
                {
                    row.Project,row.Namespace,row.Type,row.Member,row.Kind,
                    Percent(row.Coverage.LineCoverage),Percent(row.Coverage.BranchCoverage),Number(row.CrapScore),
                    row.CyclomaticComplexity.ToString(CultureInfo.InvariantCulture),
                    row.MaintainabilityIndex.ToString(CultureInfo.InvariantCulture),
                    row.ClassCoupling.ToString(CultureInfo.InvariantCulture),
                    row.MatchStatus,Location(row.File,row.StartLine)
                }));
            SectionEnd(html);

            SectionStart(html, "Afferent and efferent coupling", "dependencies");
            Table(html,
                ["Scope","Unit","Ca","Ce","Instability","Internal dependencies","External dependencies"],
                data.Dependencies.Select(row => new[]
                {
                    row.Scope,row.Unit,row.AfferentCoupling.ToString(CultureInfo.InvariantCulture),
                    row.EfferentCoupling.ToString(CultureInfo.InvariantCulture),Number(row.Instability),
                    string.Join(", ",row.InternalDependencies),string.Join(", ",row.ExternalDependencies)
                }));
            SectionEnd(html);

            html.Append("<section><h2>Definitions and provenance</h2>");
            foreach ((string name, string value) in data.Definitions)
            {
                html.Append("<p><strong>").Append(E(name)).Append(":</strong> ").Append(E(value)).Append("</p>");
            }
            html.Append("<details open><h3>Tool versions</h3><ul>");
            foreach ((string name, string version) in data.ToolVersions)
            {
                html.Append("<li><code>").Append(E(name)).Append("</code>: ").Append(E(version)).Append("</li>");
            }
            html.Append("</ul></details><details><h3>Excluded scope</h3><ul>");
            foreach (string exclusion in data.Exclusions)
            {
                html.Append("<li>").Append(E(exclusion)).Append("</li>");
            }
            html.Append("</ul></details></section>");

            html.Append("""
<footer>Generated offline from committed source and exact-revision coverage evidence. No external resources are loaded.</footer>
</div>
<script>
for(const section of document.querySelectorAll('section')){
 const table=section.querySelector('table'); if(!table) continue;
 const box=document.createElement('div'); box.className='toolbar';
 const input=document.createElement('input'); input.placeholder='Filter this table'; box.appendChild(input);
 section.insertBefore(box,table.parentElement);
 input.addEventListener('input',()=>{const q=input.value.toLowerCase();for(const row of table.tBodies[0].rows){row.hidden=!row.textContent.toLowerCase().includes(q)}});
 for(const [index,th] of [...table.tHead.rows[0].cells].entries()){
  let asc=true;th.addEventListener('click',()=>{const rows=[...table.tBodies[0].rows];rows.sort((a,b)=>a.cells[index].textContent.localeCompare(b.cells[index].textContent,undefined,{numeric:true})*(asc?1:-1));asc=!asc;for(const row of rows)table.tBodies[0].appendChild(row)});
 }
}
</script></body></html>
""");
            return html.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static void Card(StringBuilder html, string title, string value, string detail)
            => html.Append("<div class=\"card\"><span>").Append(E(title)).Append("</span><strong>")
                .Append(E(value)).Append("</strong><small>").Append(E(detail)).Append("</small></div>");

        private static void SectionStart(StringBuilder html, string title, string id)
            => html.Append("<section id=\"").Append(E(id)).Append("\"><h2>").Append(E(title))
                .Append("</h2><div class=\"table-wrap\">");

        private static void SectionEnd(StringBuilder html) => html.Append("</div></section>");

        private static void Table(
            StringBuilder html,
            IReadOnlyList<string> headings,
            IEnumerable<string[]> rows,
            int? rowClassIndex = null)
        {
            html.Append("<table><thead><tr>");
            foreach (string heading in headings) { html.Append("<th>").Append(E(heading)).Append("</th>"); }
            html.Append("</tr></thead><tbody>");
            foreach (string[] row in rows)
            {
                string css = rowClassIndex.HasValue ? row[rowClassIndex.Value].ToLowerInvariant() : string.Empty;
                html.Append("<tr");
                if (!string.IsNullOrEmpty(css)) { html.Append(" class=\"").Append(E(css)).Append('"'); }
                html.Append('>');
                foreach (string cell in row) { html.Append("<td>").Append(E(cell)).Append("</td>"); }
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");
        }

        private static string Percent(double? value)
            => value.HasValue ? $"{value.Value.ToString("0.00", CultureInfo.InvariantCulture)}%" : "n/a";

        private static string Number(double? value)
            => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a";

        private static string Location(string file, int line)
            => string.IsNullOrEmpty(file) ? "n/a" : line > 0 ? $"{file}:{line}" : file;

        private static string E(string value)
            => System.Net.WebUtility.HtmlEncode(value);
    }
}
