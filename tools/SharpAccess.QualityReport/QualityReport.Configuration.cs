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
    private sealed record Arguments(
        string RepositoryRoot,
        string RepositoryUrl,
        string Revision,
        string PolicyPath,
        string CoveragePath,
        string ComplexityPath,
        string OutputDirectory,
        string ReportGeneratorVersion)
    {
        public static Arguments Parse(string[] args)
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Arguments must use '--name value' pairs.");
                }
                values[args[index][2..]] = args[index + 1];
            }

            string Get(string name)
                => values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : throw new ArgumentException($"Missing required argument --{name}.");

            return new Arguments(
                Get("repository-root"),
                Get("repository-url"),
                Get("revision"),
                Get("policy"),
                Get("coverage"),
                Get("complexity"),
                Get("output"),
                Get("report-generator-version"));
        }
    }
    private sealed record ProjectPolicy(string Path, string Assembly, string Classification);
    private sealed record ThresholdPolicy(
        string Name,
        double Warning,
        double Critical,
        string Enforcement,
        string Direction);
    private sealed record QualityPolicy(
        int SchemaVersion,
        string Enforcement,
        int TopHotspots,
        IReadOnlyList<ProjectPolicy> Projects,
        IReadOnlyDictionary<string, ThresholdPolicy> Thresholds)
    {
        public static QualityPolicy Load(string path)
        {
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            XElement root = document.Root ?? throw new InvalidOperationException("Quality policy has no root element.");

            int schemaVersion = ParseInt(root.Descendants("QualityReportSchemaVersion").Single().Value);
            string enforcement = root.Descendants("QualityReportEnforcement").Single().Value.Trim();
            int topHotspots = ParseInt(root.Descendants("QualityReportTopHotspots").Single().Value);

            ProjectPolicy[] projects = root.Descendants("QualityReportProject")
                .Select(element => new ProjectPolicy(
                    RequiredAttribute(element, "Include"),
                    RequiredAttribute(element, "Assembly"),
                    RequiredAttribute(element, "Classification")))
                .OrderBy(project => project.Assembly, Ordinal)
                .ToArray();

            if (projects.Length == 0)
            {
                throw new InvalidOperationException("Quality policy declares no production projects.");
            }

            SortedDictionary<string, ThresholdPolicy> thresholds = new(StringComparer.Ordinal);
            foreach (XElement element in root.Descendants("QualityReportThreshold"))
            {
                string name = RequiredAttribute(element, "Include");
                thresholds[name] = new ThresholdPolicy(
                    name,
                    ParseDouble(RequiredAttribute(element, "Warning")),
                    ParseDouble(RequiredAttribute(element, "Critical")),
                    RequiredAttribute(element, "Enforcement"),
                    element.Attribute("Direction")?.Value.Trim() ?? "Maximum");
            }

            return new QualityPolicy(schemaVersion, enforcement, topHotspots, projects, thresholds);
        }

        private static string RequiredAttribute(XElement element, string name)
        {
            string? value = element.Attribute(name)?.Value;
            return !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new InvalidOperationException($"{element.Name} is missing {name}.");
        }

        private static int ParseInt(string value)
            => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static double ParseDouble(string value)
            => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
