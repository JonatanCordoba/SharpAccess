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
    private sealed record CoverageMetric(
        string Assembly,
        string Namespace,
        string Type,
        string Method,
        string Signature,
        string File,
        int StartLine,
        int CoveredLines,
        int TotalLines,
        int CoveredBranches,
        int TotalBranches)
    {
        public double? LineCoverage => TotalLines == 0 ? null : Math.Round(100.0 * CoveredLines / TotalLines, 2);
        public double? BranchCoverage => TotalBranches == 0 ? null : Math.Round(100.0 * CoveredBranches / TotalBranches, 2);
    }
    private sealed record CoverageDataset(
        int CoveredLines,
        int TotalLines,
        int CoveredBranches,
        int TotalBranches,
        IReadOnlyList<CoverageMetric> Methods)
    {
        public static CoverageDataset Load(string path, string root)
        {
            XDocument document = XDocument.Load(path, LoadOptions.None);
            XElement coverage = document.Root ?? throw new InvalidOperationException("Coverage XML has no root element.");
            int coveredLines = AttributeInt(coverage, "lines-covered");
            int totalLines = AttributeInt(coverage, "lines-valid");
            int coveredBranches = AttributeInt(coverage, "branches-covered");
            int totalBranches = AttributeInt(coverage, "branches-valid");
            List<CoverageMetric> methods = [];

            foreach (XElement package in coverage.Descendants("package"))
            {
                string assembly = package.Attribute("name")?.Value ?? string.Empty;
                foreach (XElement @class in package.Descendants("class"))
                {
                    string type = @class.Attribute("name")?.Value ?? string.Empty;
                    string file = (@class.Attribute("filename")?.Value ?? string.Empty).Replace('\\', '/');
                    if (Path.IsPathRooted(file))
                    {
                        file = Path.GetRelativePath(root, file).Replace('\\', '/');
                    }
                    string ns = type.Contains('.')
                        ? type[..type.LastIndexOf('.')]
                        : string.Empty;

                    foreach (XElement method in @class.Descendants("method"))
                    {
                        XElement[] lineElements = method.Descendants("line").ToArray();
                        int methodCoveredLines = lineElements.Count(line => AttributeInt(line, "hits") > 0);
                        int methodTotalLines = lineElements.Select(line => AttributeInt(line, "number")).Distinct().Count();
                        (int branchCovered, int branchTotal) = CountBranches(lineElements);
                        int startLine = lineElements.Length == 0
                            ? 0
                            : lineElements.Min(line => AttributeInt(line, "number"));

                        methods.Add(new CoverageMetric(
                            assembly,
                            ns,
                            type,
                            method.Attribute("name")?.Value ?? string.Empty,
                            method.Attribute("signature")?.Value ?? string.Empty,
                            file,
                            startLine,
                            methodCoveredLines,
                            methodTotalLines,
                            branchCovered,
                            branchTotal));
                    }
                }
            }

            return new CoverageDataset(
                coveredLines,
                totalLines,
                coveredBranches,
                totalBranches,
                methods.OrderBy(method => method.Assembly, Ordinal)
                    .ThenBy(method => method.Type, Ordinal)
                    .ThenBy(method => method.Method, Ordinal)
                    .ThenBy(method => method.Signature, Ordinal)
                    .ThenBy(method => method.StartLine)
                    .ToArray());
        }

        private static (int Covered, int Total) CountBranches(IEnumerable<XElement> lines)
        {
            int covered = 0;
            int total = 0;
            foreach (XElement line in lines.Where(candidate =>
                         string.Equals(candidate.Attribute("branch")?.Value, "true", StringComparison.OrdinalIgnoreCase)))
            {
                string value = line.Attribute("condition-coverage")?.Value ?? string.Empty;
                int open = value.IndexOf('(');
                int slash = value.IndexOf('/');
                int close = value.IndexOf(')');
                if (open >= 0 && slash > open && close > slash
                    && int.TryParse(value.AsSpan(open + 1, slash - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemCovered)
                    && int.TryParse(value.AsSpan(slash + 1, close - slash - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemTotal))
                {
                    covered += itemCovered;
                    total += itemTotal;
                }
            }
            return (covered, total);
        }

        private static int AttributeInt(XElement element, string name)
            => int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
    }
    private sealed record CoverageSummary(
        int CoveredLines,
        int UncoveredLines,
        int TotalLines,
        double? LineCoverage,
        int CoveredBranches,
        int UncoveredBranches,
        int TotalBranches,
        double? BranchCoverage);
}
