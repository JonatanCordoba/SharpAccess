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
    private sealed record Threshold(double Warning, double Critical);
    private sealed record QualityThresholds(
        int TopHotspots,
        Threshold Crap,
        Threshold Complexity,
        Threshold Maintainability,
        Threshold Coupling)
    {
        public static QualityThresholds Load(string path)
        {
            XDocument document = XDocument.Load(path);
            XElement root = document.Root ?? throw new InvalidOperationException("Quality policy has no root element.");
            int topHotspots = int.Parse(
                root.Descendants("QualityReportTopHotspots").Single().Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
            Dictionary<string, Threshold> thresholds = root.Descendants("QualityReportThreshold")
                .ToDictionary(
                    element => RequiredAttribute(element, "Include"),
                    element => new Threshold(
                        ParseDouble(RequiredAttribute(element, "Warning")),
                        ParseDouble(RequiredAttribute(element, "Critical"))),
                    StringComparer.Ordinal);
            return new QualityThresholds(
                topHotspots,
                Get(thresholds, "CrapScore"),
                Get(thresholds, "CyclomaticComplexity"),
                Get(thresholds, "MaintainabilityIndex"),
                Get(thresholds, "ClassCoupling"));
        }

        private static Threshold Get(IReadOnlyDictionary<string, Threshold> values, string name)
            => values.TryGetValue(name, out Threshold? value)
                ? value
                : throw new InvalidOperationException($"Quality threshold is missing: {name}");

        private static string RequiredAttribute(XElement element, string name)
            => element.Attribute(name)?.Value is { Length: > 0 } value
                ? value.Trim()
                : throw new InvalidOperationException($"{element.Name} is missing {name}.");

        private static double ParseDouble(string value)
            => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
