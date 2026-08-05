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
    private sealed record ComplexityMetric(
        string Assembly,
        string Type,
        string Method,
        string Signature,
        string File,
        int StartLine,
        int EndLine,
        int CyclomaticComplexity,
        double LineCoverage,
        double CrapScore);
    private sealed record ComplexityDataset(IReadOnlyList<ComplexityMetric> Methods)
    {
        public static ComplexityDataset Load(string path)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement methodsElement = document.RootElement.GetProperty("methods");
            List<ComplexityMetric> methods = [];
            foreach (JsonElement element in methodsElement.EnumerateArray())
            {
                methods.Add(new ComplexityMetric(
                    GetString(element, "Assembly"),
                    GetString(element, "Class"),
                    GetString(element, "Method"),
                    GetString(element, "Signature"),
                    GetString(element, "File"),
                    GetInt(element, "StartLine"),
                    GetInt(element, "EndLine"),
                    GetInt(element, "CyclomaticComplexity"),
                    GetDouble(element, "LineCoverage"),
                    GetDouble(element, "CrapScore")));
            }
            return new ComplexityDataset(
                methods.OrderBy(method => method.Assembly, Ordinal)
                    .ThenBy(method => method.Type, Ordinal)
                    .ThenBy(method => method.Method, Ordinal)
                    .ThenBy(method => method.Signature, Ordinal)
                    .ThenBy(method => method.StartLine)
                    .ToArray());
        }

        private static JsonElement GetProperty(JsonElement element, string name)
            => element.EnumerateObject()
                .First(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                .Value;

        private static string GetString(JsonElement element, string name)
            => GetProperty(element, name).GetString() ?? string.Empty;

        private static int GetInt(JsonElement element, string name)
            => GetProperty(element, name).ValueKind == JsonValueKind.Number
                ? GetProperty(element, name).GetInt32()
                : int.Parse(GetString(element, name), CultureInfo.InvariantCulture);

        private static double GetDouble(JsonElement element, string name)
            => GetProperty(element, name).ValueKind == JsonValueKind.Number
                ? GetProperty(element, name).GetDouble()
                : double.Parse(GetString(element, name), CultureInfo.InvariantCulture);
    }
}
